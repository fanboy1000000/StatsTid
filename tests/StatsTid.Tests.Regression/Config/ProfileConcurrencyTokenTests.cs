using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixtures #17–#18 — ETag/If-Match optimistic concurrency on the profile PUT
/// (ADR-017 D2.1). The endpoint maps <see cref="OptimisticConcurrencyException"/>
/// (raised by <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(System.Nullable{System.Guid}, LocalAgreementProfile, System.Threading.CancellationToken)"/>)
/// to HTTP 412 Precondition Failed; these tests pin the underlying exception contract
/// across the two scenarios:
///
/// <list type="bullet">
///   <item>#17 — caller does not provide a precondition (omits If-Match) when a current
///   profile already exists. The endpoint returns 412 from the
///   "missing-If-Match-when-current-exists" guard via the same
///   <c>OptimisticConcurrencyException</c> path; we replicate by sending
///   <c>expectedCurrentProfileId: null</c>, which is the repo-level signal for
///   "If-None-Match: *" (assert no current).</item>
///   <item>#18 — two admins racing with the same If-Match: first wins, second's
///   precondition is now stale. Repo throws <see cref="OptimisticConcurrencyException"/>
///   carrying the actual current id (= the new profile from admin 1).</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileConcurrencyTokenTests : IAsyncLifetime
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
    public async Task PutWithoutIfMatchHeader_WhenCurrentExists_Returns412()
    {
        // Seed one open profile (P1).
        var p1 = await SeedOpenProfileAsync(weeklyNormHours: 37m);

        // Caller PUTs without If-Match → endpoint translates to expectedCurrentProfileId=null
        // (the "first creation" precondition). The repository sees a current row exists
        // and raises OptimisticConcurrencyException — the same path the endpoint maps to 412.
        var candidate = NewCandidate(weeklyNormHours: 36m);
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
            await _repo.SupersedeAndCreateAsync(
                expectedCurrentProfileId: null, candidate));

        Assert.Null(ex.ExpectedProfileId);
        Assert.Equal(p1, ex.ActualProfileId);
    }

    [Fact]
    public async Task PutWithStaleIfMatch_AfterRacingAdminCommitted_Returns412()
    {
        // Seed P1.
        var p1 = await SeedOpenProfileAsync(weeklyNormHours: 37m);

        // Admin A1 supersedes with If-Match: P1 → succeeds, creates P2.
        var candidateA = NewCandidate(weeklyNormHours: 36m);
        var p2 = await _repo.SupersedeAndCreateAsync(
            expectedCurrentProfileId: p1, candidateA);
        Assert.NotEqual(Guid.Empty, p2);
        Assert.NotEqual(p1, p2);

        // Admin A2 sends a PUT still based on P1 (stale). Repo finds the current row is
        // now P2 — precondition mismatch → OptimisticConcurrencyException with actual=P2.
        var candidateB = NewCandidate(weeklyNormHours: 35m);
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
            await _repo.SupersedeAndCreateAsync(
                expectedCurrentProfileId: p1, candidateB));

        Assert.Equal(p1, ex.ExpectedProfileId);
        Assert.Equal(p2, ex.ActualProfileId);

        // Confirm: only one open profile, and it is P2 (admin A2's stale write was rejected).
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(p2, current!.ProfileId);
    }

    private async Task<Guid> SeedOpenProfileAsync(decimal weeklyNormHours)
    {
        var initial = NewCandidate(weeklyNormHours);
        return await _repo.SupersedeAndCreateAsync(
            expectedCurrentProfileId: null, initial);
    }

    private static LocalAgreementProfile NewCandidate(decimal weeklyNormHours) => new()
    {
        ProfileId = Guid.NewGuid(),
        OrgId = OrgId,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        EffectiveFrom = new DateOnly(2026, 5, 4), // Monday, alignment-safe
        WeeklyNormHours = weeklyNormHours,
        CreatedBy = "admin",
        CreatedAt = DateTime.UtcNow,
    };
}
