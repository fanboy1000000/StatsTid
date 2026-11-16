using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — AC-C2p's read half: when two versions of a key are dated the same day, the one that
/// took effect LAST wins, even when its transaction started first. Plain language: two admins can publish or
/// edit on the same day; "later" must mean the moment the change became effective (the commit-time stamp),
/// not the moment a request began, otherwise the earlier-started request could silently win. The pay half
/// (16–31 March 2026 paid on the later publish) is TASK-14828-T's. Docker-gated: CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SameDayTieBreakTests : DatedConfigDockerFixture
{
    private static readonly DateOnly Monday9Nov2026 = new(2026, 11, 9);

    /// <summary>
    /// AC-C2p (1). Clock Monday 9 November 2026. K / OK26: v2 (37 h) published with no date, then v3 (32 h)
    /// published with no date: both are dated 9 November (a week-read change on a Monday). The in-force read
    /// for Monday 9 November returns v3 (32 h).
    ///
    /// <para>
    /// <b>Kind: N (CI; M-15)</b>: it names <see cref="StatsTid.Infrastructure.AgreementConfigRepository.GetInForceAsync"/>.
    /// <b>Red conditions</b> (M-15, the tie-break inverted: <c>published_at</c> ascending): the read returns v2,
    /// so the v3 assertion trips. M-9, M-10, M-14 and M-16 leave it green (no archived draft, no seeded config,
    /// no override, and the two publishes are separate transactions, so <c>NOW()</c> still orders them).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TwoPublishesSameDayDateOmitted_ReadReturnsTheLaterPublish()
    {
        using var admin = AdminAt(Monday9Nov2026);
        var code = UniqueCode("K_TIE1_");

        await CreateAndPublishAsync(admin, ConfigBody(code, weeklyNorm: 37m), appliesFrom: null);
        var v3 = await CreateAndPublishAsync(admin, ConfigBody(code, weeklyNorm: 32m), appliesFrom: null);

        var inForce = await ConfigRepo.GetInForceAsync(code, Ok26, Monday9Nov2026);
        Assert.NotNull(inForce);
        Assert.Equal(v3, inForce!.ConfigId);
        Assert.Equal(32m, inForce.WeeklyNormHours);
    }

    /// <summary>
    /// AC-C2p (2), the reversed-start shape. Repository level, dates explicit. An override for (K, OK26, P)
    /// created dated Monday 2 November 2026 (cap 100). Transaction B is opened and its first statement, a plain
    /// <c>SELECT 1</c>, run (Npgsql sends the BEGIN with the first command, so the fixture names that command);
    /// transaction A edits cap 110 dated Monday 9 November and commits; B then edits cap 130 dated Monday 9
    /// November, at the version A left, and commits. The read for Monday 9 November returns 130: B, the later
    /// write, though its transaction started first.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-15)</b>: it names the re-signed override writers and
    /// <see cref="StatsTid.Infrastructure.PositionOverrideRepository.GetInForceAsync"/>. <b>Red conditions</b> (M-15,
    /// the override's <c>recorded_at</c> ordering inverted): the read returns A's 110, so the 130 assertion trips.
    /// M-14 (the head returned) leaves it green, since the head holds the later write: that is NR-P15's reason
    /// the two mutations run apart.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OverrideEditedTwiceOnOneDate_ResolvesTheLaterWrite()
    {
        var code = UniqueCode("K_TIE2_");
        var position = await SeedPositionAsync();
        var monday2Nov = new DateOnly(2026, 11, 2);

        Guid overrideId;
        await using (var conn = Db.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            overrideId = await OverrideRepo.CreateAsync(conn, tx, NewOverride(code, position, 100m), monday2Nov);
            await tx.CommitAsync();
        }

        // Transaction B starts first: its BEGIN is sent with its first command.
        await using var connB = Db.Create();
        await connB.OpenAsync();
        await using var txB = await connB.BeginTransactionAsync();
        await using (var begin = new Npgsql.NpgsqlCommand("SELECT 1", connB, txB))
            await begin.ExecuteScalarAsync();

        // Transaction A starts later and commits first.
        await using (var connA = Db.Create())
        {
            await connA.OpenAsync();
            await using var txA = await connA.BeginTransactionAsync();
            var resultA = await OverrideRepo.UpdateAsync(
                connA, txA, overrideId, expectedVersion: 1, NewOverride(code, position, 110m), Monday9Nov2026, "tester-a");
            await txA.CommitAsync();
            Assert.Equal(2L, resultA.Version);
        }

        // B edits at the version A left and commits second.
        await OverrideRepo.UpdateAsync(
            connB, txB, overrideId, expectedVersion: 2, NewOverride(code, position, 130m), Monday9Nov2026, "tester-b");
        await txB.CommitAsync();

        var inForce = await OverrideRepo.GetInForceAsync(code, Ok26, position, Monday9Nov2026);
        Assert.NotNull(inForce);
        Assert.Equal(130m, inForce!.MaxFlexBalance);
    }

    /// <summary>
    /// AC-C2p (4), the reversed-start shape for publishes. Repository level. K / OK26: v0 published dated Monday
    /// 2 November 2026; drafts X and Y (same norm). Transaction A is opened and its first statement, a plain
    /// <c>SELECT 1</c>, run; a second transaction publishes Y dated Monday 9 November and commits; A then
    /// publishes X dated Monday 9 November and commits. <b>First assertion:</b> X's <c>published_at</c> is later
    /// than Y's. <b>Second:</b> the read for Monday 9 November returns X. The fact publishes its own drafts and
    /// reads no seeded configuration (NR-P24's condition, so M-10 cannot turn it red).
    ///
    /// <para>
    /// <b>Kind: N (CI; M-15 at CI-M4a, M-16 at CI-M4b)</b>. <b>Red conditions</b>: M-16 (the activating UPDATE
    /// stamps <c>published_at = NOW()</c>, A's transaction start, which is earlier than Y's) trips the first
    /// assertion only (the stamps then order X before Y); M-15 (the tie-break inverted) leaves the stamps right
    /// and trips the second assertion (the read returns Y). M-9 leaves it green (no archived draft).
    /// </para>
    /// </summary>
    [Fact]
    public async Task EarlierStartedPublishCommittingLater_IsTheOneInForce()
    {
        var code = UniqueCode("K_TIE4_");
        await RepoCreateAndPublishAsync(NewConfig(code), new DateOnly(2026, 11, 2));
        var x = await ConfigRepo.CreateAsync(NewConfig(code));
        var y = await ConfigRepo.CreateAsync(NewConfig(code));
        var xVersion = (await ConfigRepo.GetByIdAsync(x))!.Version;
        var yVersion = (await ConfigRepo.GetByIdAsync(y))!.Version;

        // Transaction A starts first: its BEGIN is sent with its first command.
        await using var connA = Db.Create();
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync();
        await using (var begin = new Npgsql.NpgsqlCommand("SELECT 1", connA, txA))
            await begin.ExecuteScalarAsync();

        // A second transaction publishes Y and commits.
        await using (var connY = Db.Create())
        {
            await connY.OpenAsync();
            await using var txY = await connY.BeginTransactionAsync();
            await ConfigRepo.PublishAsync(connY, txY, y, yVersion, Monday9Nov2026, "tester-y");
            await txY.CommitAsync();
        }

        // A publishes X, the same date, and commits last.
        await ConfigRepo.PublishAsync(connA, txA, x, xVersion, Monday9Nov2026, "tester-x");
        await txA.CommitAsync();

        var xStamp = await ScalarAsync<DateTime>("SELECT published_at FROM agreement_configs WHERE config_id = @id", ("id", x));
        var yStamp = await ScalarAsync<DateTime>("SELECT published_at FROM agreement_configs WHERE config_id = @id", ("id", y));
        Assert.True(xStamp > yStamp,
            $"X's published_at ({xStamp:O}) must be later than Y's ({yStamp:O}): the stamp is the moment of the publish, not the start of A's transaction");

        var inForce = await ConfigRepo.GetInForceAsync(code, Ok26, Monday9Nov2026);
        Assert.NotNull(inForce);
        Assert.Equal(x, inForce!.ConfigId);
    }

    private static PositionOverrideConfigEntity NewOverride(string agreementCode, string positionCode, decimal maxFlex) => new()
    {
        OverrideId = Guid.Empty,
        AgreementCode = agreementCode,
        OkVersion = Ok26,
        PositionCode = positionCode,
        Status = "ACTIVE",
        MaxFlexBalance = maxFlex,
        NormPeriodWeeks = 4,
        WeeklyNormHours = null,
        CreatedBy = "tester",
        Description = "S148 same-day tie-break",
    };
}
