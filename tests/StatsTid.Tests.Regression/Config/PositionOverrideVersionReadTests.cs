using System.Net;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — AC-C2o: a position override's dated versions, read by key. Plain language: when an
/// override's flex cap is raised, switched off or switched back on "from" a Monday, a recalculation of an
/// earlier day must still see the state that applied then, so every write keeps a version row and one read
/// answers "what did this override say on day X?". The facts read no agreement configuration (the override
/// routes and the by-key read touch none), so M-10 cannot turn them red for another reason (NR-P15).
/// Docker-gated: CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverrideVersionReadTests : DatedConfigDockerFixture
{
    private static readonly DateOnly Monday16Nov2026 = new(2026, 11, 16);

    /// <summary>
    /// AC-C2o (4): the head row equals the newest version row (state, values and date); asserted after every
    /// write of the three facts, which between them create, edit, deactivate and activate.
    /// </summary>
    private async Task AssertHeadEqualsNewestVersionAsync(Guid overrideId, string when)
    {
        var equal = await CountAsync(
            """
            SELECT COUNT(*)
            FROM position_override_configs h
            JOIN LATERAL (
                SELECT * FROM position_override_config_versions v
                WHERE v.override_id = h.override_id
                ORDER BY v.applies_from DESC, v.recorded_at DESC, v.version_id DESC
                LIMIT 1) n ON TRUE
            WHERE h.override_id = @id
              AND h.status = n.state
              AND h.applies_from = n.applies_from
              AND h.max_flex_balance IS NOT DISTINCT FROM n.max_flex_balance
              AND h.norm_period_weeks IS NOT DISTINCT FROM n.norm_period_weeks
              AND h.weekly_norm_hours IS NOT DISTINCT FROM n.weekly_norm_hours
            """, ("id", overrideId));
        Assert.True(equal == 1L, $"after {when}: the head row must equal the key's newest version row");
    }

    private static async Task AssertOkAsync(HttpResponseMessage response, string what)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{what} expected 200, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// AC-C2o (1). Clock Monday 16 November 2026 (on or after every date). Through the routes with explicit
    /// dates: create cap 100 dated Monday 26 October 2026; edit cap 120 dated Monday 2 November. The by-key
    /// read returns 100 for Friday 30 October and 120 for Monday 2 November. (4) after each write.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-14)</b>: it names <see cref="StatsTid.Infrastructure.PositionOverrideRepository.GetInForceAsync"/>.
    /// <b>Red conditions</b> (M-14, the in-force read returns the head row instead of the newest version on or
    /// before the day): Friday 30 October reads 120, the head's value, not 100, so the first read trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CapEditedFromMonday2November2026_Friday30OctoberReads100_2NovemberReads120()
    {
        using var admin = AdminAt(Monday16Nov2026);
        var code = UniqueCode("K_OVR1_");
        var position = await SeedPositionAsync();

        var (id, v1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, position, 100m, null, "2026-10-26"));
        await AssertHeadEqualsNewestVersionAsync(id, "the create");

        using var edit = await EditOverrideAsync(admin, id, v1, OverrideBody(code, Ok26, position, 120m, null, "2026-11-02"));
        await AssertOkAsync(edit, "the edit");
        await AssertHeadEqualsNewestVersionAsync(id, "the edit");

        var friday30Oct = await OverrideRepo.GetInForceAsync(code, Ok26, position, new DateOnly(2026, 10, 30));
        Assert.NotNull(friday30Oct);
        Assert.Equal(100m, friday30Oct!.MaxFlexBalance);
        var monday2Nov = await OverrideRepo.GetInForceAsync(code, Ok26, position, new DateOnly(2026, 11, 2));
        Assert.NotNull(monday2Nov);
        Assert.Equal(120m, monday2Nov!.MaxFlexBalance);
    }

    /// <summary>
    /// AC-C2o (2). Same clock. Create cap 120 dated Monday 26 October; deactivate dated Monday 9 November.
    /// Friday 6 November reads 120; Monday 9 November reads none. (4) after each write.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-14)</b>. <b>Red conditions</b> (M-14, the head row returned): the head is INACTIVE
    /// after the deactivation, so Friday 6 November reads none, not 120, and the first read trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeactivatedFromMonday9November_6NovemberReads120_9thNone()
    {
        using var admin = AdminAt(Monday16Nov2026);
        var code = UniqueCode("K_OVR2_");
        var position = await SeedPositionAsync();

        var (id, v1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, position, 120m, null, "2026-10-26"));
        await AssertHeadEqualsNewestVersionAsync(id, "the create");

        using var off = await TransitionOverrideAsync(admin, id, v1, "deactivate", "2026-11-09");
        await AssertOkAsync(off, "the deactivation");
        await AssertHeadEqualsNewestVersionAsync(id, "the deactivation");

        var friday6Nov = await OverrideRepo.GetInForceAsync(code, Ok26, position, new DateOnly(2026, 11, 6));
        Assert.NotNull(friday6Nov);
        Assert.Equal(120m, friday6Nov!.MaxFlexBalance);
        Assert.Null(await OverrideRepo.GetInForceAsync(code, Ok26, position, new DateOnly(2026, 11, 9)));
    }

    /// <summary>
    /// AC-C2o (3). Same clock. Create cap 120 dated 26 October; deactivate dated Monday 9 November; activate
    /// dated Monday 16 November. Sunday 15 November reads none; Monday 16 November reads 120. (4) after each
    /// write.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-14)</b>. <b>Red conditions</b> (M-14, the head row returned): the head is ACTIVE after
    /// the activation, so Sunday 15 November reads 120, not none, and the first read trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ReactivatedFromMonday16November_15thNone_16thReads120()
    {
        using var admin = AdminAt(Monday16Nov2026);
        var code = UniqueCode("K_OVR3_");
        var position = await SeedPositionAsync();

        var (id, v1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, position, 120m, null, "2026-10-26"));
        await AssertHeadEqualsNewestVersionAsync(id, "the create");

        using var off = await TransitionOverrideAsync(admin, id, v1, "deactivate", "2026-11-09");
        await AssertOkAsync(off, "the deactivation");
        await AssertHeadEqualsNewestVersionAsync(id, "the deactivation");

        using var on = await TransitionOverrideAsync(admin, id, EtagOf(off), "activate", "2026-11-16");
        await AssertOkAsync(on, "the activation");
        await AssertHeadEqualsNewestVersionAsync(id, "the activation");

        Assert.Null(await OverrideRepo.GetInForceAsync(code, Ok26, position, new DateOnly(2026, 11, 15)));
        var monday16Nov = await OverrideRepo.GetInForceAsync(code, Ok26, position, new DateOnly(2026, 11, 16));
        Assert.NotNull(monday16Nov);
        Assert.Equal(120m, monday16Nov!.MaxFlexBalance);
    }
}
