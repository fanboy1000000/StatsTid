using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixtures #17–#18 — ETag/If-Match optimistic concurrency on the profile PUT
/// (S22 / ADR-018 D7 update — row-version replaces profile_id as the concurrency
/// token). The endpoint maps <see cref="OptimisticConcurrencyException"/> (raised by
/// <see cref="LocalAgreementProfileRepository"/>'s SupersedeAndCreateAsync) to HTTP
/// 412 Precondition Failed; these tests pin the underlying exception contract across
/// the two scenarios:
///
/// <list type="bullet">
///   <item>#17 — caller does not provide a precondition (omits If-Match) when a current
///   profile already exists. The endpoint returns 412 from the
///   "missing-If-Match-when-current-exists" guard via the same
///   <c>OptimisticConcurrencyException</c> path; we replicate by sending
///   <c>expectedCurrentVersion: null</c>, which is the repo-level signal for
///   "If-None-Match: *" (assert no current).</item>
///   <item>#18 — two admins racing with the same If-Match: first wins, second's
///   precondition is now stale. Repo throws <see cref="OptimisticConcurrencyException"/>
///   carrying the actual current version (= the new profile's version after admin 1's
///   write).</item>
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
        // Seed one open profile (P1, initial version=1).
        var (_, p1Version) = await SeedOpenProfileAsync(weeklyNormHours: 37m);

        // Caller PUTs without If-Match → endpoint translates to expectedCurrentVersion=null
        // (the "first creation" precondition). The repository sees a current row exists
        // and raises OptimisticConcurrencyException — the same path the endpoint maps to 412.
        var candidate = NewCandidate(weeklyNormHours: 36m);
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
            await _repo.SupersedeAndCreateAsync(
                expectedCurrentVersion: null, candidate));

        Assert.Null(ex.ExpectedVersion);
        Assert.Equal(p1Version, ex.ActualVersion);
    }

    [Fact]
    public async Task PutWithStaleIfMatch_AfterRacingAdminCommitted_Returns412()
    {
        // Seed P1 at version 1 (effective_from = 2026-05-04).
        var (p1Id, p1Version) = await SeedOpenProfileAsync(weeklyNormHours: 37m);
        Assert.Equal(1L, p1Version);

        // Admin A1 saves with If-Match: "1" using the same effective_from = 2026-05-04.
        // Same-day save routes to UPDATE-in-place (ADR-018 D9 MODIFIED branch): profile_id
        // is stable (still P1) and version bumps from 1 → 2.
        var candidateA = NewCandidate(weeklyNormHours: 36m);
        var (afterAId, afterAVersion) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: p1Version, candidateA);
        Assert.Equal(p1Id, afterAId);          // UPDATE-in-place keeps the same profile_id
        Assert.Equal(2L, afterAVersion);       // version bumped

        // Admin A2 still has the stale If-Match: "1" (read before A1's commit). Repo finds
        // the current row's version is now 2 → precondition mismatch → throws.
        var candidateB = NewCandidate(weeklyNormHours: 35m);
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
            await _repo.SupersedeAndCreateAsync(
                expectedCurrentVersion: p1Version, candidateB));

        Assert.Equal(p1Version, ex.ExpectedVersion);
        Assert.Equal(afterAVersion, ex.ActualVersion);

        // Confirm: still only one open profile, and it is the post-A1 row at version 2
        // (admin A2's stale write was rejected — no third row inserted).
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(p1Id, current!.ProfileId);
        Assert.Equal(afterAVersion, current.Version);
    }

    private async Task<(Guid ProfileId, long Version)> SeedOpenProfileAsync(decimal weeklyNormHours)
    {
        var initial = NewCandidate(weeklyNormHours);
        return await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null, initial);
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
        Version = 1,
    };
}
