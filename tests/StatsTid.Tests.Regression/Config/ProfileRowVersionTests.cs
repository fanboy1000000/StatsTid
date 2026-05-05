using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D12 fixtures #6–#10 — row-version optimistic concurrency, UPDATE-in-place,
/// end-exclusive supersession, and the same-day-supersession-then-modification audit
/// chain. Exercises the post-S22 <see cref="LocalAgreementProfileRepository"/> surface
/// (ADR-018 D7 / D8 / D9) end-to-end against a real Postgres testcontainer.
///
/// <para>
/// Same-day vs cross-day routing is determined by comparing
/// <c>newProfile.EffectiveFrom</c> to the predecessor's <c>effective_from</c>:
/// equal → UPDATE-in-place (audit MODIFIED); strictly greater → close-then-insert
/// (audit SUPERSEDED); strictly less → <see cref="InvalidProfileSupersessionException"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileRowVersionTests : IAsyncLifetime
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
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        _repo = new LocalAgreementProfileRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task InPlaceUpdate_BumpsVersion()
    {
        // Seed P1 at version 1 with effective_from = today.
        var monday = new DateOnly(2026, 5, 4); // Monday — alignment-safe
        var (profileId, v1) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));
        Assert.Equal(1L, v1);

        // PUT changes WeeklyNormHours but keeps the same effective_from → UPDATE-in-place.
        var candidate = NewProfile(monday, weeklyNormHours: 36m);
        var (returnedId, v2) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: v1, candidate);

        Assert.Equal(profileId, returnedId);   // profile_id stable
        Assert.Equal(2L, v2);                  // version bumped

        // Read back: effective_from unchanged, WeeklyNormHours updated, version = 2.
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(profileId, current!.ProfileId);
        Assert.Equal(monday, current.EffectiveFrom);
        Assert.Equal(36m, current.WeeklyNormHours);
        Assert.Equal(2L, current.Version);
        Assert.Null(current.EffectiveTo);
    }

    [Fact]
    public async Task ConcurrentInPlace_StaleIfMatchReturns412_NoOutboxRow()
    {
        // Admin A and B both load at V=1. A saves (V → 2). B saves with stale If-Match=1.
        // B's repo call throws OptimisticConcurrencyException → endpoint returns 412.
        // Because the (production) PUT handler only calls EnqueueAsync inside the same
        // tx that the repo throws inside, B's failed call writes no outbox row.
        var monday = new DateOnly(2026, 5, 4);
        var (_, v1) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));
        Assert.Equal(1L, v1);

        // Admin A saves (UPDATE-in-place since same effective_from).
        var (_, v2) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: v1,
            NewProfile(monday, weeklyNormHours: 36m));
        Assert.Equal(2L, v2);

        // Admin B's stale-V1 save races. We model the production PUT-handler shape:
        // open a tx, attempt the supersede call (throws), and would-have-called
        // EnqueueAsync after success. Since the throw aborts the flow, no outbox
        // row is enqueued. The endpoint catches OCC and rolls back.
        var enqueueStore = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("backend-api"));
        var preCount = await CountOutboxRowsAsync();
        var bCandidate = NewProfile(monday, weeklyNormHours: 35m);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            try
            {
                await _repo.SupersedeAndCreateAsync(
                    conn, tx, expectedCurrentVersion: v1, bCandidate);

                // Production-shape: only enqueue after the repo call returns. Unreachable
                // here because the previous call throws.
                var evt = new LocalAgreementProfileChanged
                {
                    ProfileId = bCandidate.ProfileId,
                    OrgId = OrgId,
                    AgreementCode = AgreementCode,
                    OkVersion = OkVersion,
                    EffectiveFrom = monday,
                    ActorId = "adminB",
                    ActorRole = "LocalAdmin",
                };
                await enqueueStore.EnqueueAsync(conn, tx, "test-stream", evt);
                await tx.CommitAsync();
                Assert.Fail("Expected OptimisticConcurrencyException, but the flow committed.");
            }
            catch (OptimisticConcurrencyException ex)
            {
                Assert.Equal(v1, ex.ExpectedVersion);
                Assert.Equal(v2, ex.ActualVersion);
                await tx.RollbackAsync();
            }
        }

        // Outbox row count delta from B's failed save = 0.
        var postCount = await CountOutboxRowsAsync();
        Assert.Equal(preCount, postCount);
    }

    [Fact]
    public async Task SameDaySave_RoutesToUpdateInPlace()
    {
        // Seed profile with effective_from = today; save with same effective_from + a
        // changed value. One row exists for the (org, agreement, ok_version) triple,
        // its effective_to is NULL, and the audit-action that the production endpoint
        // would emit is MODIFIED. The repo doesn't write audit rows itself; we verify
        // the audit-action MODIFIED is acceptable to the schema (CHECK constraint
        // includes 'MODIFIED' post-S22) by inserting a manual audit row alongside.
        var monday = new DateOnly(2026, 5, 4);
        var (profileId, v1) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(monday, weeklyNormHours: 37m));

        var (sameId, v2) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: v1,
            NewProfile(monday, weeklyNormHours: 36m));

        Assert.Equal(profileId, sameId);
        Assert.Equal(2L, v2);

        // Exactly one row for the triple.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var countCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM local_agreement_profiles
            WHERE org_id = @org AND agreement_code = @ac AND ok_version = @ok
            """, conn))
        {
            countCmd.Parameters.AddWithValue("org", OrgId);
            countCmd.Parameters.AddWithValue("ac", AgreementCode);
            countCmd.Parameters.AddWithValue("ok", OkVersion);
            Assert.Equal(1L, Convert.ToInt64(await countCmd.ExecuteScalarAsync()));
        }

        // effective_to IS NULL.
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Null(current!.EffectiveTo);

        // Audit-action MODIFIED is accepted by the post-S22 CHECK constraint.
        await using var auditCmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profile_audit
                (profile_id, action, delta_jsonb, actor_id, actor_role)
            VALUES (@id, 'MODIFIED', '{}'::jsonb, 'admin1', 'LocalAdmin')
            """, conn);
        auditCmd.Parameters.AddWithValue("id", profileId);
        var rows = await auditCmd.ExecuteNonQueryAsync();
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task EndExclusiveSupersedeClosePredecessor()
    {
        // Predecessor effective_from = X (Monday 2025-12-29). Save with effective_from = Y
        // (Monday 2026-05-04) > X → close-then-insert (SUPERSEDED). Predecessor's
        // effective_to is stamped at Y (NOT Y-1, end-exclusive per ADR-018 D8). The
        // active-on-Y query returns the new row, not the predecessor.
        var x = new DateOnly(2025, 12, 29);
        var y = new DateOnly(2026, 5, 4);

        var (predId, predV) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(x, weeklyNormHours: 37m));

        var (newId, newV) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: predV,
            NewProfile(y, weeklyNormHours: 36m));

        Assert.NotEqual(predId, newId);
        Assert.Equal(1L, newV); // close-then-insert always starts at version 1

        // Predecessor's effective_to = Y exactly.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var predCmd = new NpgsqlCommand(
            "SELECT effective_to FROM local_agreement_profiles WHERE profile_id = @id", conn))
        {
            predCmd.Parameters.AddWithValue("id", predId);
            var raw = await predCmd.ExecuteScalarAsync();
            Assert.NotNull(raw);
            Assert.NotEqual(DBNull.Value, raw);
            var effectiveTo = DateOnly.FromDateTime((DateTime)raw!);
            Assert.Equal(y, effectiveTo);
        }

        // "Active on Y" query (end-exclusive: effective_from <= Y AND (effective_to IS NULL OR Y < effective_to))
        // returns the new row, not the predecessor.
        await using (var activeCmd = new NpgsqlCommand(
            """
            SELECT profile_id FROM local_agreement_profiles
            WHERE org_id = @org AND agreement_code = @ac AND ok_version = @ok
              AND effective_from <= @d
              AND (effective_to IS NULL OR @d < effective_to)
            """, conn))
        {
            activeCmd.Parameters.AddWithValue("org", OrgId);
            activeCmd.Parameters.AddWithValue("ac", AgreementCode);
            activeCmd.Parameters.AddWithValue("ok", OkVersion);
            activeCmd.Parameters.AddWithValue("d", y);
            var activeId = (Guid?)await activeCmd.ExecuteScalarAsync();
            Assert.Equal(newId, activeId);
        }
    }

    [Fact]
    public async Task SameDaySupersession_ThenInPlaceModification()
    {
        // Sequence: profile created at X-7 (CREATED), supersession PUT at X creates new
        // row (SUPERSEDED), next PUT at X UPDATE-in-place on the new row (MODIFIED).
        // Final row's version = 2.
        var x_minus_7 = new DateOnly(2026, 4, 27);  // Monday
        var x = new DateOnly(2026, 5, 4);            // Monday

        // Step 1: CREATE at x-7.
        var (origId, origV) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: null,
            NewProfile(x_minus_7, weeklyNormHours: 37m));
        Assert.Equal(1L, origV);
        await InsertAuditAsync(origId, "CREATED");

        // Step 2: SUPERSEDE at x — close orig, insert new row at version 1.
        var (newId, newV) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: origV,
            NewProfile(x, weeklyNormHours: 36m));
        Assert.NotEqual(origId, newId);
        Assert.Equal(1L, newV);
        await InsertAuditAsync(newId, "SUPERSEDED");

        // Step 3: MODIFY at x — UPDATE-in-place on the new row, version 1 → 2.
        var (sameId, finalV) = await _repo.SupersedeAndCreateAsync(
            expectedCurrentVersion: newV,
            NewProfile(x, weeklyNormHours: 35m));
        Assert.Equal(newId, sameId);
        Assert.Equal(2L, finalV);
        await InsertAuditAsync(newId, "MODIFIED");

        // Audit chain: CREATED → SUPERSEDED → MODIFIED, in timestamp order.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var auditCmd = new NpgsqlCommand(
            """
            SELECT action FROM local_agreement_profile_audit
            WHERE profile_id IN (@orig, @new)
            ORDER BY audit_id ASC
            """, conn);
        auditCmd.Parameters.AddWithValue("orig", origId);
        auditCmd.Parameters.AddWithValue("new", newId);
        var actions = new List<string>();
        await using var reader = await auditCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            actions.Add(reader.GetString(0));
        Assert.Equal(new[] { "CREATED", "SUPERSEDED", "MODIFIED" }, actions);

        // Final row version = 2.
        var current = await _repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(newId, current!.ProfileId);
        Assert.Equal(2L, current.Version);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static LocalAgreementProfile NewProfile(DateOnly effectiveFrom, decimal weeklyNormHours) => new()
    {
        ProfileId = Guid.NewGuid(),
        OrgId = OrgId,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        EffectiveFrom = effectiveFrom,
        WeeklyNormHours = weeklyNormHours,
        CreatedBy = "admin",
        CreatedAt = DateTime.UtcNow,
        Version = 1,
    };

    private async Task<long> CountOutboxRowsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM outbox_events", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task InsertAuditAsync(Guid profileId, string action)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profile_audit
                (profile_id, action, delta_jsonb, actor_id, actor_role)
            VALUES (@id, @action, '{}'::jsonb, 'admin', 'LocalAdmin')
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("action", action);
        await cmd.ExecuteNonQueryAsync();
    }
}
