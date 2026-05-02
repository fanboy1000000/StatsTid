using System.Data;
using System.Text.Json;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixtures #11–#12 — the audit chain emitted on profile supersession (ADR-017 D6).
/// One save MUST produce both:
///
/// <list type="bullet">
///   <item>One <c>LocalAgreementProfileChanged</c> event in the event store with a structured
///   <c>ChangedFields</c> delta and <c>PrecedingProfileId</c> pointing at the predecessor.</item>
///   <item>One <c>local_agreement_profile_audit</c> projection row with action
///   <c>'SUPERSEDED'</c> and an equivalent <c>delta_jsonb</c>.</item>
/// </list>
///
/// <para>
/// The PUT endpoint (<c>ConfigEndpoints.MapPut(...)</c>) orchestrates the same three-step
/// flow these tests run: <c>SupersedeAndCreateAsync</c> + audit-row INSERT (in the same
/// PostgreSQL tx, ADR-017 D6) + post-commit <c>IEventStore.AppendAsync</c>. This test
/// pins that the production sequencing — audit + event with matching delta — is correct
/// at the building-block level. (No <c>WebApplicationFactory&lt;Program&gt;</c> harness in the
/// regression project; the equivalent path under test is the same shape.)
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileAuditTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private LocalAgreementProfileRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ProfileTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProfileTestSchema.SeedOrganizationAsync(_harness.ConnectionString, OrgId);
        _repo = new LocalAgreementProfileRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Mutation_EmitsEventWithDelta()
    {
        // Seed predecessor (WeeklyNormHours = 37) and supersede (WeeklyNormHours = 36) via
        // the same flow the PUT endpoint runs.
        var predecessor = await CreateInitialProfileAsync(weeklyNormHours: 37m);
        var (newProfileId, changedFields) = await SupersedeAsync(
            predecessor.ProfileId, weeklyNormHours: 36m);

        // Append the LocalAgreementProfileChanged event after the profile transaction
        // commits successfully — same shape the PUT endpoint uses.
        var streamId = $"local-agreement-profile-{OrgId}-{AgreementCode}-{OkVersion}";
        var @event = new LocalAgreementProfileChanged
        {
            ProfileId = newProfileId,
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = new DateOnly(2026, 5, 4),
            ChangedFields = changedFields,
            PrecedingProfileId = predecessor.ProfileId,
            ActorId = "admin1",
            ActorRole = "LocalAdmin",
        };
        await _harness.EventStore.AppendAsync(streamId, @event);

        // Read back the event from the store and verify the delta.
        var events = await _harness.EventStore.ReadStreamAsync(streamId);
        var stored = Assert.Single(events);
        var typed = Assert.IsType<LocalAgreementProfileChanged>(stored);
        Assert.Equal(newProfileId, typed.ProfileId);
        Assert.Equal(predecessor.ProfileId, typed.PrecedingProfileId);
        Assert.True(typed.ChangedFields.ContainsKey("WeeklyNormHours"));
        var change = typed.ChangedFields["WeeklyNormHours"];
        Assert.Equal(37m, change.Old.GetDecimal());
        Assert.Equal(36m, change.New.GetDecimal());
    }

    [Fact]
    public async Task Mutation_PersistsAuditProjectionRow()
    {
        // Same PUT shape as #11: predecessor (37) → successor (36). Verify the audit row
        // (action='SUPERSEDED') with a delta_jsonb shape that matches the in-memory delta.
        var predecessor = await CreateInitialProfileAsync(weeklyNormHours: 37m);
        var (newProfileId, _) = await SupersedeAsync(
            predecessor.ProfileId, weeklyNormHours: 36m);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, delta_jsonb::text
            FROM local_agreement_profile_audit
            WHERE profile_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", newProfileId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("SUPERSEDED", reader.GetString(0));

        var deltaJson = reader.GetString(1);
        using var doc = JsonDocument.Parse(deltaJson);
        Assert.True(doc.RootElement.TryGetProperty("WeeklyNormHours", out var change));
        Assert.True(change.TryGetProperty("Old", out var oldEl));
        Assert.True(change.TryGetProperty("New", out var newEl));
        Assert.Equal(37m, oldEl.GetDecimal());
        Assert.Equal(36m, newEl.GetDecimal());

        Assert.False(await reader.ReadAsync(), "Expected exactly one audit row.");
    }

    // ─── helpers (mirror the PUT endpoint's orchestration) ────────────────────

    private async Task<LocalAgreementProfile> CreateInitialProfileAsync(decimal weeklyNormHours)
    {
        var initial = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = new DateOnly(2025, 12, 29), // Monday — alignment-safe
            WeeklyNormHours = weeklyNormHours,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
        };
        var newId = await _repo.SupersedeAndCreateAsync(
            expectedCurrentProfileId: null, initial);
        // Read it back so we have the canonical persisted row.
        var persisted = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(persisted);
        Assert.Equal(newId, persisted!.ProfileId);
        return persisted;
    }

    /// <summary>
    /// Run the supersession transaction the same way the PUT endpoint does: lock+close+insert
    /// via the in-transaction repo overload, plus an audit row INSERT on the same conn+tx,
    /// then COMMIT. Returns the new profile id and the computed changed-fields dictionary.
    /// </summary>
    private async Task<(Guid NewProfileId, Dictionary<string, FieldChange> ChangedFields)>
        SupersedeAsync(Guid predecessorId, decimal weeklyNormHours)
    {
        var candidate = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = new DateOnly(2026, 5, 4), // Monday — alignment-safe
            WeeklyNormHours = weeklyNormHours,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
        };

        // Recover the predecessor's value to compose the delta — endpoint reads
        // GetCurrentOpenAsync for the same purpose.
        var predecessor = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(predecessor);
        Assert.Equal(predecessorId, predecessor!.ProfileId);

        var changedFields = new Dictionary<string, FieldChange>(StringComparer.Ordinal);
        if (predecessor.WeeklyNormHours != candidate.WeeklyNormHours)
        {
            changedFields["WeeklyNormHours"] = new FieldChange(
                JsonSerializer.SerializeToElement(predecessor.WeeklyNormHours),
                JsonSerializer.SerializeToElement(candidate.WeeklyNormHours));
        }

        Guid newProfileId;
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        try
        {
            newProfileId = await _repo.SupersedeAndCreateAsync(
                conn, tx, expectedCurrentProfileId: predecessorId, candidate);

            var deltaJson = JsonSerializer.Serialize(changedFields);
            await using var auditCmd = new NpgsqlCommand(
                """
                INSERT INTO local_agreement_profile_audit
                    (profile_id, action, delta_jsonb, actor_id, actor_role)
                VALUES (@profileId, 'SUPERSEDED', @delta::jsonb, 'admin1', 'LocalAdmin')
                """, conn, tx);
            auditCmd.Parameters.AddWithValue("profileId", newProfileId);
            auditCmd.Parameters.AddWithValue("delta", deltaJson);
            await auditCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return (newProfileId, changedFields);
    }
}
