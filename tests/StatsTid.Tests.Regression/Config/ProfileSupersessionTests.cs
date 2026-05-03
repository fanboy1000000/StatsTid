using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// Step-7a Codex BLOCKER regressions on <see cref="LocalAgreementProfileRepository"/>'s
/// supersession path:
///
/// <list type="bullet">
///   <item>Backdated supersession must close the predecessor at
///   <c>newProfile.EffectiveFrom - 1 day</c> — not at the wall-clock <c>today</c> —
///   so historical reads and <c>BoundarySources</c> hydration don't see overlapping
///   open windows for the (org_id, agreement_code, ok_version) triple.</item>
///   <item>Concurrent first-creation race (two <c>If-None-Match: *</c> requests for
///   the same triple) must surface as <see cref="OptimisticConcurrencyException"/>,
///   not as a raw <see cref="PostgresException"/> with SqlState 23505. The PUT
///   handler maps the former to 412 Precondition Failed; without translation, the
///   latter would surface to clients as a 500.</item>
/// </list>
///
/// <para>
/// These tests exercise the repository (not the schema directly), so they
/// complement <see cref="ProfileUniquenessTests"/> which pins the schema-level
/// partial-unique-index invariant.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileSupersessionTests : IAsyncLifetime
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
    public async Task BackdatedSupersession_ClosesPredecessorAtNewEffectiveFromMinusOne()
    {
        // Predecessor effective from a Monday in early 2025; replacement backdated
        // to a Monday late in 2025 (still before today=2026-05-03). The endpoint
        // allows backdated saves (D2 only rejects future effective_from), so the
        // predecessor's effective_to MUST be the day before the replacement starts,
        // not the wall-clock today (which would create an overlapping window).
        var predecessorEffectiveFrom = new DateOnly(2025, 1, 6);   // Monday
        var newEffectiveFrom = new DateOnly(2025, 12, 29);          // Monday, before today
        var expectedPredecessorEffectiveTo = newEffectiveFrom.AddDays(-1); // 2025-12-28 (Sunday)

        var predecessor = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = predecessorEffectiveFrom,
            WeeklyNormHours = 37m,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
        };
        var predecessorId = await _repo.SupersedeAndCreateAsync(
            expectedCurrentProfileId: null, predecessor);

        var replacement = new LocalAgreementProfile
        {
            ProfileId = Guid.NewGuid(),
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = newEffectiveFrom,
            WeeklyNormHours = 36m,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.SupersedeAndCreateAsync(predecessorId, replacement);

        // Read the predecessor row back and assert effective_to was stamped at
        // newEffectiveFrom - 1 day, NOT at today.
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT effective_to FROM local_agreement_profiles WHERE profile_id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", predecessorId);
        var raw = await cmd.ExecuteScalarAsync();
        Assert.NotNull(raw);
        Assert.NotEqual(DBNull.Value, raw);
        var actualEffectiveTo = DateOnly.FromDateTime((DateTime)raw!);

        Assert.Equal(expectedPredecessorEffectiveTo, actualEffectiveTo);

        // Defensive: the replacement is the only currently-open profile for the triple.
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(replacement.ProfileId, current!.ProfileId);
        Assert.Equal(newEffectiveFrom, current.EffectiveFrom);
        Assert.Null(current.EffectiveTo);
    }

    [Fact]
    public async Task ConcurrentFirstCreate_RaceTranslatesToOptimisticConcurrency()
    {
        // Race condition: connA opens a RepeatableRead tx FIRST and pins a snapshot
        // showing an empty active slot. connB then commits an INSERT outside connA's
        // snapshot. connA proceeds with SupersedeAndCreateAsync(null, …):
        //
        //   - AcquireLockAsync returns null (snapshot doesn't see connB's commit).
        //   - ValidatePrecondition passes (expected=null, actual=null).
        //   - InsertProfileAsync trips PostgreSQL's partial-unique-index
        //     uq_local_agreement_profile_active because the index check sees
        //     committed-by-other rows regardless of MVCC visibility.
        //
        // The repository MUST translate that 23505 into OptimisticConcurrencyException
        // so the PUT handler's existing catch-and-map-to-412 path still works under
        // the empty-slot race (D2.1 contract). Pre-cycle-1 the raw PostgresException
        // bubbled up and surfaced as a 500.
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2026, 5, 4); // Monday, alignment-safe

        await using var connA = _harness.Factory.Create();
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync(IsolationLevel.RepeatableRead);

        // Force connA's snapshot to be taken NOW (RepeatableRead snapshots are taken
        // on the first non-transactional statement). A SELECT 1 is enough.
        await using (var snapCmd = new NpgsqlCommand("SELECT 1", connA, txA))
        {
            await snapCmd.ExecuteScalarAsync();
        }

        // connB inserts a row with effective_to=NULL and commits — outside connA's
        // snapshot. Use raw INSERT (not the repo) so connA's race partner is
        // unambiguously another writer, not a recursive call into the repo.
        await using (var connB = _harness.Factory.Create())
        {
            await connB.OpenAsync();
            await using var insertB = NewInsertCmd(connB, null, idB, effectiveFrom);
            await insertB.ExecuteNonQueryAsync();
        }

        var pA = new LocalAgreementProfile
        {
            ProfileId = idA,
            OrgId = OrgId,
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EffectiveFrom = effectiveFrom,
            WeeklyNormHours = 37m,
            CreatedBy = "admin1",
            CreatedAt = DateTime.UtcNow,
        };

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            async () => await _repo.SupersedeAndCreateAsync(
                connA, txA, expectedCurrentProfileId: null, pA));

        Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23505", ((PostgresException)ex.InnerException!).SqlState);
        Assert.Equal(
            "uq_local_agreement_profile_active",
            ((PostgresException)ex.InnerException!).ConstraintName);

        // The throw left connA's tx open; roll it back to release server resources.
        await txA.RollbackAsync();
    }

    private static NpgsqlCommand NewInsertCmd(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid profileId, DateOnly effectiveFrom)
    {
        var cmd = tx is null
            ? new NpgsqlCommand("", conn)
            : new NpgsqlCommand("", conn, tx);
        cmd.CommandText = """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by)
            VALUES (
                @id, @org, @ac, @ok,
                @from, NULL,
                NULL, 100, NULL, NULL, NULL,
                'test')
            """;
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        return cmd;
    }
}
