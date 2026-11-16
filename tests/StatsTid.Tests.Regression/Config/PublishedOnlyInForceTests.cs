namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — AC-C2m's read-and-predecessor half: only a PUBLISHED version is ever in force or a
/// predecessor. Plain language: a draft that was dated and then archived without ever being published must
/// never decide what applied on a day, and must never block a later publish. The pay half (May 2026 paid under
/// the published version) is TASK-14828-T's. Docker-gated: CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class PublishedOnlyInForceTests : DatedConfigDockerFixture
{
    /// <summary>
    /// AC-C2m. Clock Monday 9 November 2026. K / OK26: v1 (37 h) published dated Monday 6 April 2026. A DRAFT D
    /// (32 h) has its date set to Monday 4 May 2026 by SQL (no route dates a draft) and is archived through the
    /// route (a DRAFT, so 200). <b>Read:</b> the in-force read for Monday 4 May and for Friday 29 May returns v1.
    /// <b>Predecessor:</b> v2 (37 h) published dated Monday 20 April 2026 is accepted (200), and the read for
    /// Monday 4 May then returns v2. The fact publishes its own versions and reads no seeded configuration.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-9)</b>: it names <see cref="StatsTid.Infrastructure.AgreementConfigRepository.GetInForceAsync"/>,
    /// which TASK-14818 creates. <b>Red conditions</b> (M-9, the in-force read and the predecessor rule select by
    /// status alone, without <c>published_at IS NOT NULL</c>): the archived draft D is dated 4 May, so both May
    /// reads return D, not v1 (the read assertions trip), and v2's publish dated 20 April is refused
    /// <c>BEFORE_PREDECESSOR</c> against D's 4 May (the predecessor assertion trips). Not turned red by M-15 or
    /// M-16 (no same-day publishes here), nor by M-10 (the fact reads no seeded configuration).
    /// </para>
    /// </summary>
    [Fact]
    public async Task ArchivedDraft_NeverInForce_NeverPredecessor()
    {
        using var admin = AdminAt(new DateOnly(2026, 11, 9));
        var code = UniqueCode("K_ARCHD_");

        var v1 = await CreateAndPublishAsync(admin, ConfigBody(code, weeklyNorm: 37m), "2026-04-06");

        var (draft, draftVersion) = await CreateDraftAsync(admin, ConfigBody(code, weeklyNorm: 32m));
        await ExecAsync(
            "UPDATE agreement_configs SET applies_from = DATE '2026-05-04' WHERE config_id = @id", ("id", draft));
        using (var archive = await admin.SendAsync(Contracts.SpecRuntimeTestSupport.JsonRequest(
                   HttpMethod.Post, $"/api/agreement-configs/{draft}/archive", jsonBody: null, ifMatchVersion: draftVersion)))
        {
            Assert.Equal(200, (int)archive.StatusCode);
        }
        Assert.Equal("ARCHIVED", await StatusOfConfigAsync(draft));

        // Read: the archived, never-published draft is never in force.
        foreach (var day in new[] { new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 29) })
        {
            var inForce = await ConfigRepo.GetInForceAsync(code, Ok26, day);
            Assert.NotNull(inForce);
            Assert.Equal(v1, inForce!.ConfigId);
            Assert.Equal(37m, inForce.WeeklyNormHours);
        }

        // Predecessor: the same draft never blocks a publish dated before its own date.
        var v2 = await CreateAndPublishAsync(admin, ConfigBody(code, weeklyNorm: 37m), "2026-04-20");
        var afterV2 = await ConfigRepo.GetInForceAsync(code, Ok26, new DateOnly(2026, 5, 4));
        Assert.NotNull(afterV2);
        Assert.Equal(v2, afterV2!.ConfigId);
    }
}
