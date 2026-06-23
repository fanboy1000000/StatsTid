using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S97 / TASK-9704 / ADR-035 — bootstrap backfill that migrates the legacy free-text
/// <c>employee_profiles.enhed_label</c> projection column into the structured
/// <c>enheder</c> entity table + the <c>user_enheder</c> multi-tag membership link, via
/// the dedicated <see cref="EnhedCreated"/> / <see cref="UserEnhederChanged"/> events
/// (event-sourced — NO raw projection INSERT; the S92 lesson, restated as Step-0b BLOCKER A).
///
/// <para>
/// <b>Source = the PROJECTION column, NOT an event replay (Step-0b BLOCKER A).</b> Real
/// labels live ONLY in the demo seed, which wrote <c>employee_profiles</c> rows by a raw
/// projection INSERT and NEVER emitted <c>EmployeeProfileCreated.EnhedLabel</c> events. So
/// this seeder reads the live projection column directly:
/// <code>
/// SELECT DISTINCT u.primary_org_id, ep.enhed_label
/// FROM employee_profiles ep
/// JOIN users u ON u.user_id = ep.employee_id
/// WHERE ep.effective_to IS NULL          -- current live profiles only
///   AND ep.enhed_label IS NOT NULL
///   AND TRIM(ep.enhed_label) &lt;&gt; ''
/// </code>
/// </para>
///
/// <para>
/// <b>Greenfield = NO-OP by design (Step-0b BLOCKER A).</b> In the init.sql greenfield
/// baseline (what CI reseeds), <c>enhed_label</c> is universally NULL — S92 deliberately
/// did NOT pre-seed it, and <see cref="EmployeeProfileSeeder"/> inserts NULL. The source
/// query therefore returns ZERO rows and this seeder is a pure no-op. The migration test
/// (TASK-9707) MUST seed ≥1 labeled profile (or run against the demo dataset) so the
/// "no user loses metadata" guarantee is non-vacuous — a CI run with the all-NULL baseline
/// proves nothing about the migration path. This is the LEGACY-DB-UPGRADE mechanism (see
/// docs/operations/legacy-db-upgrade-runbook.md), not a greenfield seeder.
/// </para>
///
/// <para>
/// <b>Idempotent.</b> Re-running must not duplicate enheder or tags. Two guards:
/// (1) per DISTINCT (org, label) we look up an EXISTING active enhed with that
/// <c>(organisation_id, lower(name))</c> — the same key as the partial-unique
/// <c>idx_enheder_active_name</c> — and reuse its id instead of emitting a second
/// <see cref="EnhedCreated"/>; (2) per user we compare the desired single-tag set against
/// the user's CURRENT <c>user_enheder</c> set and skip the <see cref="UserEnhederChanged"/>
/// emit when they already match. A concurrent-startup race on the active-name unique index
/// (<c>23505</c>) is caught and re-resolved to the winner's id (same semantic as the other
/// bootstrap seeders).
/// </para>
///
/// <para>
/// <b>Ordering + atomicity.</b> EnhedCreated is committed BEFORE the UserEnhederChanged
/// that references its id. Each enhed-create and each user-tag-set rides its own per-row
/// atomic tx (projection write + outbox event in one transaction; ADR-018 D3) so a failure
/// on one row does not strand the rest. Stream ids match the event contracts:
/// <c>enhed-{enhedId}</c> for EnhedCreated, <c>user-{userId}</c> for UserEnhederChanged.
/// </para>
///
/// <para>
/// Runs AFTER <see cref="EmployeeProfileSeeder"/> in Program.cs (it depends on the live
/// employee_profiles rows existing) and AFTER the init.sql users seed.
/// </para>
/// </summary>
public static class EnhedBackfillSeeder
{
    public static async Task SeedAsync(
        DbConnectionFactory dbFactory,
        IOutboxEnqueue outbox,
        EnhedRepository enhedRepository,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);

        // ── 1. Source: DISTINCT (Organisation, label) from current live profiles. ──
        //
        // Reads the PROJECTION column (Step-0b BLOCKER A) — the demo seed never emitted
        // EmployeeProfileCreated.EnhedLabel, so an event replay would migrate nothing.
        // The JOIN through users.primary_org_id is already ORGANISATION-constrained post-S92
        // (a user's primary_org IS an ORGANISATION), so every backfilled enhed is created
        // under an ORGANISATION-typed parent — no org-type guard needed here (WARNING D).
        await using var distinctCmd = new NpgsqlCommand(
            """
            SELECT DISTINCT u.primary_org_id, TRIM(ep.enhed_label) AS label
            FROM employee_profiles ep
            JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.effective_to IS NULL
              AND ep.enhed_label IS NOT NULL
              AND TRIM(ep.enhed_label) <> ''
            ORDER BY u.primary_org_id, label
            """, conn);

        var distinctLabels = new List<(string OrgId, string Label)>();
        await using (var reader = await distinctCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                distinctLabels.Add((reader.GetString(0), reader.GetString(1)));
        }

        if (distinctLabels.Count == 0)
        {
            // Greenfield no-op (Step-0b BLOCKER A): the CI baseline is all-NULL enhed_label.
            logger.LogDebug(
                "Enhed backfill: no live profiles carry an enhed_label — nothing to migrate (greenfield no-op)");
            return;
        }

        logger.LogInformation(
            "Enhed backfill: {Count} distinct (Organisation, label) pairs to reconcile from enhed_label...",
            distinctLabels.Count);

        // ── 2. Reconcile enheder: reuse an existing active enhed or emit EnhedCreated. ──
        //
        // enhedIdByKey maps (orgId, lower(label)) -> the active enhed_id to tag users with.
        var enhedIdByKey = new Dictionary<(string OrgId, string LowerLabel), Guid>();
        var created = 0;
        var reused = 0;

        foreach (var (orgId, label) in distinctLabels)
        {
            var key = (orgId, label.ToLowerInvariant());

            // Idempotency guard (1): an active enhed with this (org, lower(name)) already
            // exists from a prior run — reuse its id, do NOT emit a second EnhedCreated.
            var existingId = await FindActiveEnhedIdAsync(conn, orgId, label, ct);
            if (existingId is not null)
            {
                enhedIdByKey[key] = existingId.Value;
                reused++;
                continue;
            }

            var enhedId = Guid.NewGuid();
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                var @event = new EnhedCreated
                {
                    EnhedId = enhedId,
                    OrganisationId = orgId,
                    Name = label,
                    ActorId = "SYSTEM_SEED",
                    ActorRole = "SYSTEM",
                    CorrelationId = null,
                };
                // Projection write + outbox event in ONE tx (ADR-018 D3). The writer's
                // INSERT may hit idx_enheder_active_name (23505) if a concurrent startup
                // won the same key — handled below.
                await enhedRepository.ApplyEnhedCreatedAsync(conn, tx, @event, ct);
                await outbox.EnqueueAsync(conn, tx, $"enhed-{enhedId}", @event, ct);
                await tx.CommitAsync(ct);

                enhedIdByKey[key] = enhedId;
                created++;
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                // Concurrent-startup race on idx_enheder_active_name: another instance
                // created this (org, lower(name)) first. Re-resolve to the winner's id so
                // the user-tag phase still tags against a real, active enhed.
                await tx.RollbackAsync(ct);
                var winnerId = await FindActiveEnhedIdAsync(conn, orgId, label, ct);
                if (winnerId is null)
                {
                    logger.LogError(
                        "Enhed backfill: 23505 on ({Org}, '{Label}') but no active enhed resolves afterward — rethrowing",
                        orgId, label);
                    throw;
                }
                enhedIdByKey[key] = winnerId.Value;
                reused++;
                logger.LogWarning(
                    "Enhed backfill: lost a concurrent-startup race creating ({Org}, '{Label}') — reusing the winner's enhed_id",
                    orgId, label);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Enhed backfill: failed to create enhed ({Org}, '{Label}') — rolling back this row",
                    orgId, label);
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // ── 3. Tag each live, labeled user with their single resolved enhed. ──
        //
        // One UserEnhederChanged per user carrying the FULL set (here: the single backfilled
        // tag). Idempotency guard (2): skip the emit when the user's current set already
        // equals the desired set (a re-run, or a tag added out-of-band).
        await using var usersCmd = new NpgsqlCommand(
            """
            SELECT ep.employee_id, u.primary_org_id, TRIM(ep.enhed_label) AS label
            FROM employee_profiles ep
            JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.effective_to IS NULL
              AND ep.enhed_label IS NOT NULL
              AND TRIM(ep.enhed_label) <> ''
            ORDER BY ep.employee_id
            """, conn);

        var labeledUsers = new List<(string UserId, string OrgId, string Label)>();
        await using (var reader = await usersCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                labeledUsers.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        var tagged = 0;
        var tagsAlreadyCurrent = 0;
        foreach (var (userId, orgId, label) in labeledUsers)
        {
            var key = (orgId, label.ToLowerInvariant());
            if (!enhedIdByKey.TryGetValue(key, out var enhedId))
            {
                // Should not happen — every labeled user's (org, label) was reconciled in
                // phase 2. Defensive: log + skip rather than tag against a missing enhed.
                logger.LogWarning(
                    "Enhed backfill: user '{User}' label ({Org}, '{Label}') resolved no enhed_id — skipping tag",
                    userId, orgId, label);
                continue;
            }

            // Idempotency guard (2): compare against the user's current ACTIVE tag set.
            var currentIds = await enhedRepository.GetUserActiveEnhedIdsAsync(userId, ct);
            if (currentIds.Count == 1 && currentIds[0] == enhedId)
            {
                tagsAlreadyCurrent++;
                continue;
            }

            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                var @event = new UserEnhederChanged
                {
                    UserId = userId,
                    EnhedIds = new[] { enhedId },
                    ActorId = "SYSTEM_SEED",
                    ActorRole = "SYSTEM",
                    CorrelationId = null,
                };
                // Projection (delete-all-then-insert the full set) + outbox event in ONE tx.
                await enhedRepository.ApplyUserEnhederChangedAsync(conn, tx, @event, ct);
                await outbox.EnqueueAsync(conn, tx, $"user-{userId}", @event, ct);
                await tx.CommitAsync(ct);
                tagged++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Enhed backfill: failed to tag user '{User}' with enhed {EnhedId} — rolling back this row",
                    userId, enhedId);
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        logger.LogInformation(
            "Enhed backfill complete — enheder: {Created} created, {Reused} reused; user tags: {Tagged} set, {AlreadyCurrent} already current",
            created, reused, tagged, tagsAlreadyCurrent);
    }

    /// <summary>Resolves the active (non-soft-deleted) enhed_id for a given
    /// (organisation, name) using the SAME case-folded key as
    /// <c>idx_enheder_active_name</c>, or <c>null</c> when none exists. Used by both
    /// idempotency guard (1) and the concurrent-race re-resolution.</summary>
    private static async Task<Guid?> FindActiveEnhedIdAsync(
        NpgsqlConnection conn, string organisationId, string name, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT enhed_id
            FROM enheder
            WHERE organisation_id = @org
              AND lower(name) = lower(@name)
              AND deleted_at IS NULL
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("org", organisationId);
        cmd.Parameters.AddWithValue("name", name);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (Guid)result;
    }
}
