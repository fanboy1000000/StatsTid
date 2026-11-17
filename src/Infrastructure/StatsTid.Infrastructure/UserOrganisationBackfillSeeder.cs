using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S148 C-13 (ADR-040 D4; QUAL-206) — the startup backfill of <c>user_organisations</c>: every ACTIVE
/// user with no open organisation row gets one, holding their current <c>users.primary_org_id</c>,
/// from <c>0001-01-01</c>. The <see cref="UserAgreementCodeBackfillSeeder"/> shape.
///
/// <para><b>Why it exists.</b> Users inserted by SQL (the seed users in <c>init.sql</c>, the demo world,
/// the test fixtures that do not write the row themselves) have no organisation history, and
/// <see cref="EmploymentProfileResolver"/> refuses a day a profile covers but no organisation row does.
/// This seeder is the only source of those users' organisation rows; the user create writes its own.
/// <c>0001-01-01</c> makes every past day resolve the organisation the user holds today, which is the
/// answer the resolver gave before the organisation was dated.</para>
///
/// <para><b>Idempotent, one transaction per user (ADR-018 D3).</b> Each user's row, its CREATED audit row
/// (actor <c>SYSTEM_SEED</c> / <c>SYSTEM</c>; both written by
/// <see cref="UserOrganisationRepository.InsertFirstRowAsync"/>, the single first-row write path shared
/// with the user create) and its <see cref="UserOrganisationSeeded"/> outbox event on
/// <c>user-{userId}</c> commit together. A user who already has an open row is never selected, so a
/// dated history is never overwritten. Two hosts starting at once can both select the same user; the
/// loser's insert fails on <c>idx_user_organisations_live</c> (23505) and is skipped, because the
/// winner's row is the same row. No inline audit-projection row: the startup audit backfill projects the
/// event (it resolves the target organisation from the event's <c>OrgId</c>).</para>
///
/// <para><b>Active users only</b>, as both sibling seeders: a user inactive at host start and reactivated
/// later has no organisation row until the next start (the agreement-code seeder has the same gap).</para>
/// </summary>
public static class UserOrganisationBackfillSeeder
{
    public static async Task SeedAsync(
        DbConnectionFactory dbFactory,
        IOutboxEnqueue outbox,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);

        await using var findMissingCmd = new NpgsqlCommand(
            """
            SELECT u.user_id, u.primary_org_id
            FROM users u
            WHERE u.is_active = TRUE
              AND NOT EXISTS (
                  SELECT 1 FROM user_organisations uo
                  WHERE uo.user_id = u.user_id AND uo.effective_to IS NULL
              )
            ORDER BY u.user_id
            """, conn);

        var missing = new List<(string UserId, string OrgId)>();
        await using (var reader = await findMissingCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                missing.Add((reader.GetString(0), reader.GetString(1)));
        }

        if (missing.Count == 0)
        {
            logger.LogDebug("User organisations already seeded for all active users — skipping");
            return;
        }

        logger.LogInformation("Seeding user_organisations for {Count} users without an open row...", missing.Count);

        var repository = new UserOrganisationRepository();
        var seeded = 0;
        var skippedRace = 0;
        foreach (var (userId, orgId) in missing)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                var row = await repository.InsertFirstRowAsync(
                    conn, tx, userId, orgId, actorId: "SYSTEM_SEED", actorRole: "SYSTEM", ct);

                var @event = new UserOrganisationSeeded
                {
                    UserId = userId,
                    AssignmentId = row.AssignmentId,
                    OrgId = row.OrgId,
                    EffectiveFrom = row.EffectiveFrom,
                    RowVersion = row.Version,
                    ActorId = "SYSTEM_SEED",
                    ActorRole = "SYSTEM",
                    CorrelationId = null,
                };
                await outbox.EnqueueAsync(conn, tx, $"user-{userId}", @event, ct);

                await tx.CommitAsync(ct);
                seeded++;
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "User organisation backfill for user_id='{UserId}' lost a concurrent-startup race (PostgresException 23505 on {Constraint}); another instance inserted the row first — skipping",
                    userId, pgEx.ConstraintName);
                skippedRace++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed user_organisations for user {UserId} — rolling back this row", userId);
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        logger.LogInformation(
            "User organisation seeding complete — {Seeded} rows inserted with UserOrganisationSeeded events; {SkippedRace} rows skipped due to concurrent-startup 23505 race",
            seeded, skippedRace);
    }
}
