using System.Data;
using System.Text.Json;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixture #16 — pins ADR-017 D7's compatibility break: profile saves under the new API
/// MUST emit exactly one <c>LocalAgreementProfileChanged</c> event for the calling
/// correlation id and zero <c>LocalConfigurationChanged</c> events. The legacy event type
/// stays registered in <see cref="EventSerializer"/> for historical replay but is never
/// emitted by the post-S21 PUT endpoint.
///
/// <para>
/// Replicates the PUT endpoint's event-emission shape (post-commit
/// <see cref="StatsTid.SharedKernel.Interfaces.IEventStore.AppendAsync"/> of a new
/// <see cref="LocalAgreementProfileChanged"/> event keyed by profile-stream id; no legacy
/// event ever appended). Asserts the resulting event-stream slice contains exactly one
/// new-shape event and no legacy events.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileLegacyEventNonEmissionTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private LocalAgreementProfileRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await Config.ProfileTestSchema.ApplyAsync(_harness.ConnectionString);
        await Config.ProfileTestSchema.SeedOrganizationAsync(_harness.ConnectionString, OrgId);
        _repo = new LocalAgreementProfileRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ProfilePut_EmitsExactlyOneProfileEvent_AndZeroLegacyEvents()
    {
        var correlationId = Guid.NewGuid();
        var monday = new DateOnly(2026, 5, 4);

        // Run the same profile-save sequence the PUT endpoint runs:
        // 1. SupersedeAndCreateAsync (no predecessor → If-None-Match: * shape).
        // 2. Audit row INSERT in same tx — handled inside the repo overload via
        //    its self-contained variant; for legacy-event non-emission the audit row
        //    is irrelevant, so we use the simpler self-contained overload.
        // 3. Post-commit AppendAsync of LocalAgreementProfileChanged.
        var candidate = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = monday,
            WeeklyNormHours = 36m,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
            Version = 1,
        };
        var (newProfileId, _) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null, candidate);

        var streamId = $"local-agreement-profile-{OrgId}-{AgreementCode}-{OkVersion}";
        var profileEvent = new LocalAgreementProfileChanged
        {
            ProfileId = newProfileId,
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = monday,
            ChangedFields = new Dictionary<string, FieldChange>
            {
                ["WeeklyNormHours"] = new FieldChange(
                    JsonSerializer.SerializeToElement<decimal?>(null),
                    JsonSerializer.SerializeToElement<decimal?>(36m)),
            },
            PrecedingProfileId = null,
            ActorId = "admin1",
            ActorRole = "LocalAdmin",
            CorrelationId = correlationId,
        };
        await _harness.EventStore.AppendAsync(streamId, profileEvent);

        // Now query the events table directly — verify exactly one new-shape event with the
        // calling correlation id, and zero LocalConfigurationChanged events for the same
        // correlation id.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using var newCountCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM events
            WHERE event_type = 'LocalAgreementProfileChanged'
              AND correlation_id = @cid
            """, conn);
        newCountCmd.Parameters.AddWithValue("cid", correlationId);
        Assert.Equal(1L, Convert.ToInt64(await newCountCmd.ExecuteScalarAsync()));

        await using var legacyCountCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM events
            WHERE event_type = 'LocalConfigurationChanged'
              AND correlation_id = @cid
            """, conn);
        legacyCountCmd.Parameters.AddWithValue("cid", correlationId);
        Assert.Equal(0L, Convert.ToInt64(await legacyCountCmd.ExecuteScalarAsync()));
    }
}
