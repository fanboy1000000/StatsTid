using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 5a c1 ruling 1.7 (Fable W4), AC-C2s. A second create for a key that already has an ACTIVE override
/// answers 409 with the same error text as the switch-on route, not a server error, and writes nothing.
/// Docker-gated: argued against the stack tip, run in CI (CI-W4a).
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverrideOccupiedKeyTests : DatedConfigDockerFixture
{
    private const string ActiveConflictText = "Another override is already ACTIVE for this (agreement, ok, position)";

    /// <summary>
    /// Clock Monday 9 November 2026. A create (no weekly norm, dated Monday 2 November) succeeds; a second
    /// create for the same key, dated the same day, answers 409 with the switch-on route's text. Afterwards the
    /// key has one head, one CREATED audit row and one <c>PositionOverrideCreated</c> outbox row: the second
    /// request wrote nothing.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>. <b>Red conditions</b> (argued against <c>af4325ba</c>): the create has no 23505 catch,
    /// so the unique index's refusal surfaces as a 500 and the 409 assertion fails first. The one-row counts
    /// fail if the second request's transaction were ever committed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SecondCreateOnOccupiedKey_409_NothingWritten()
    {
        using var admin = AdminAt(new DateOnly(2026, 11, 9));
        var code = UniqueCode("K_OCC_");
        var position = await SeedPositionAsync();
        var body = OverrideBody(code, Ok26, position, maxFlexBalance: 100m, weeklyNormHours: null, appliesFrom: "2026-11-02");

        await CreateOverrideAsync(admin, body);

        using var second = await admin.PostAsJsonAsync("/api/admin/position-overrides", body);
        var text = await second.Content.ReadAsStringAsync();
        Assert.True(second.StatusCode == HttpStatusCode.Conflict,
            $"a second create on an occupied key must answer 409, got {(int)second.StatusCode}: {text}");
        Assert.Equal(ActiveConflictText, JsonDocument.Parse(text).RootElement.GetProperty("error").GetString());

        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM position_override_configs WHERE agreement_code = @c AND position_code = @p",
            ("c", code), ("p", position)));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM position_override_config_audit WHERE action = 'CREATED' AND new_data ->> 'PositionCode' = @p",
            ("p", position)));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'PositionOverrideCreated' AND event_payload::text LIKE '%' || @p || '%'",
            ("p", position)));
    }
}
