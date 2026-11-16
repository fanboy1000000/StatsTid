using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S148 / TASK-14818-T (NR-P21) — the one door through which repository-level tests call the
/// re-signed override writers and <see cref="AgreementConfigRepository.PublishAsync"/>.
///
/// <para>
/// The writers now require an applies-from date. Every repository-level fixture that is not
/// about dating uses <see cref="Anchor"/>, <c>0001-01-01</c>, the seeds' own anchor: a Monday (so
/// the Monday rule never refuses it), never in the future, and never before any version of a
/// test-unique key (so the predecessor rule never refuses it). The two retired self-managed
/// writers (<c>CreateAsync(entity)</c>, <c>DeactivateAsync(Guid)</c>) are replaced by
/// <see cref="CreateAsync(PositionOverrideRepository, DbConnectionFactory, PositionOverrideConfigEntity)"/>
/// and <see cref="DeactivateAsync(PositionOverrideRepository, DbConnectionFactory, Guid)"/>, which open
/// a connection and a transaction, call the in-transaction writer and commit.
/// </para>
///
/// <para>Facts that ARE about dating pass their own dates to the repositories and do not use this.</para>
/// </summary>
internal static class PositionOverrideTestWriter
{
    /// <summary>The seeds' anchor date: a Monday, never in the future, before any test version.</summary>
    public static readonly DateOnly Anchor = new(1, 1, 1);

    /// <summary>The actor the re-signed writers record in the version row.</summary>
    public const string Actor = "tester";

    /// <summary>Replaces the retired self-managed <c>CreateAsync(entity)</c>: one connection, one commit.</summary>
    public static async Task<Guid> CreateAsync(
        PositionOverrideRepository repo, DbConnectionFactory factory, PositionOverrideConfigEntity entity)
    {
        await using var conn = factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var id = await repo.CreateAsync(conn, tx, entity, Anchor);
        await tx.CommitAsync();
        return id;
    }

    /// <summary>
    /// Replaces the retired self-managed <c>DeactivateAsync(Guid)</c>: reads the row's current version,
    /// deactivates dated <see cref="Anchor"/>, commits.
    /// </summary>
    public static async Task DeactivateAsync(
        PositionOverrideRepository repo, DbConnectionFactory factory, Guid overrideId)
    {
        var current = await repo.GetByIdAsync(overrideId)
            ?? throw new InvalidOperationException($"Override {overrideId} not found.");
        await using var conn = factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await repo.DeactivateAsync(conn, tx, overrideId, current.Version, Anchor, Actor);
        await tx.CommitAsync();
    }

    /// <summary>In-transaction create, dated <see cref="Anchor"/>.</summary>
    public static Task<Guid> CreateAsync(
        PositionOverrideRepository repo, NpgsqlConnection conn, NpgsqlTransaction tx,
        PositionOverrideConfigEntity entity)
        => repo.CreateAsync(conn, tx, entity, Anchor);

    /// <summary>In-transaction update, dated <see cref="Anchor"/>.</summary>
    public static Task<SavePositionOverrideResult> UpdateAsync(
        PositionOverrideRepository repo, NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, PositionOverrideConfigEntity updated)
        => repo.UpdateAsync(conn, tx, overrideId, expectedVersion, updated, Anchor, Actor);

    /// <summary>In-transaction deactivate, dated <see cref="Anchor"/>.</summary>
    public static Task<SavePositionOverrideResult> DeactivateAsync(
        PositionOverrideRepository repo, NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion)
        => repo.DeactivateAsync(conn, tx, overrideId, expectedVersion, Anchor, Actor);

    /// <summary>In-transaction activate, dated <see cref="Anchor"/>.</summary>
    public static Task<SavePositionOverrideResult> ActivateAsync(
        PositionOverrideRepository repo, NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion)
        => repo.ActivateAsync(conn, tx, overrideId, expectedVersion, Anchor, Actor);

    /// <summary>In-transaction agreement-config publish, dated <see cref="Anchor"/>.</summary>
    public static Task<SaveAgreementConfigResult> PublishAsync(
        AgreementConfigRepository repo, NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, long expectedVersion, string actorId)
        => repo.PublishAsync(conn, tx, configId, expectedVersion, Anchor, actorId);
}
