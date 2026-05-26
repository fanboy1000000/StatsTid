using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// Seeds the <c>employee_profiles</c> table with one live row per existing user on first boot.
/// Idempotent: reads users + existing employee_profiles, creates rows ONLY for users that
/// don't yet have a live (effective_to IS NULL) profile. Each new row commits atomically
/// with an <see cref="EmployeeProfileCreated"/> outbox event in a single transaction —
/// matching the 4-way atomicity contract that <c>POST /api/admin/users</c> uses for new
/// users in steady state (TASK-3108).
///
/// <para>
/// S31 / TASK-3106. Seeder route (vs. SQL-side INSERTs in init.sql) chosen at Step 0b
/// cycle 1 absorption because event emission requires <see cref="IOutboxEnqueue"/>
/// serialization — SQL-side INSERTs into <c>outbox_events</c> would bypass the
/// EventSerializer registry and break replay determinism.
/// </para>
///
/// <para>
/// Defaults for the 3 net-new fields:
/// <c>weekly_norm_hours = 37.0</c>, <c>part_time_fraction = 1.000</c>,
/// <c>position = NULL</c>. Admins re-enter correct values post-S31 via the new
/// <c>/api/admin/employee-profiles/{employeeId}</c> PUT (TASK-3107) — pre-launch posture
/// means no prior intent to preserve (Risk R5 in PLAN-s31.md).
/// </para>
/// </summary>
public static class EmployeeProfileSeeder
{
    private const decimal DefaultPartTimeFraction = 1.000m;

    public static async Task SeedAsync(
        DbConnectionFactory dbFactory,
        IOutboxEnqueue outbox,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);

        // Find users that lack a live employee_profiles row.
        await using var findMissingCmd = new NpgsqlCommand(
            """
            SELECT u.user_id
            FROM users u
            WHERE u.is_active = TRUE
              AND NOT EXISTS (
                  SELECT 1 FROM employee_profiles p
                  WHERE p.employee_id = u.user_id AND p.effective_to IS NULL
              )
            ORDER BY u.user_id
            """, conn);

        var missing = new List<string>();
        await using (var reader = await findMissingCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                missing.Add(reader.GetString(0));
            }
        }

        if (missing.Count == 0)
        {
            logger.LogDebug("Employee profiles already seeded for all active users — skipping");
            return;
        }

        logger.LogInformation("Seeding employee_profiles for {Count} users without live rows...", missing.Count);

        var seeded = 0;
        var skippedRace = 0;
        foreach (var employeeId in missing)
        {
            // Each seed insert rides its own atomic tx (row INSERT + outbox event in one
            // transaction; ADR-018 D5 atomic outbox pattern). Independent transactions
            // per row keep retry semantics clean if any single insert fails.
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // S33 Step 7a P1 absorption: backfill MUST use schema DEFAULT '0001-01-01'
                // (NOT today) so existing employees' historical periods resolve via the
                // resolver's `effective_from <= asOfDate` predicate. Stamping today on
                // backfill would leave pre-deployment periods uncovered → resolver returns
                // null → PCS/Compliance fail-closed with 500 on any historical calc.
                // Same-day PUT against a seeder-backfilled row routes to Case C
                // (effective_from='0001-01-01' < today), but the Step 7a P1 fix in
                // EmployeeProfileRepository.InsertLiveRowAsync now stamps
                // supersedingVersion = predecessor.Version + 1, so the ETag monotonicity
                // contract holds across the supersession.
                var profileId = Guid.NewGuid();
                await using var insertCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profiles
                        (profile_id, employee_id, part_time_fraction, position)
                    VALUES
                        (@profileId, @employeeId, @partTimeFraction, NULL)
                    """, conn, tx);
                insertCmd.Parameters.AddWithValue("profileId", profileId);
                insertCmd.Parameters.AddWithValue("employeeId", employeeId);
                insertCmd.Parameters.AddWithValue("partTimeFraction", DefaultPartTimeFraction);
                await insertCmd.ExecuteNonQueryAsync(ct);

                // Step 7a P2 fix — emit a CREATED audit row in the same per-row tx
                // so the largest migration scenario this sprint introduces (backfill
                // of all existing users) doesn't leave the audit table empty. Mirrors
                // the UPDATED audit shape at EmployeeProfileEndpoints.cs PUT path.
                // previous_data is NULL (no predecessor), version_before is NULL,
                // version_after = 1, actor_id = SYSTEM_SEED (matches the event's
                // ActorId so audit + outbox cross-reference cleanly).
                var newData = JsonSerializer.Serialize(new
                {
                    partTimeFraction = DefaultPartTimeFraction,
                    position = (string?)null,
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, 'CREATED',
                        NULL, @newData::jsonb,
                        NULL, 1,
                        'SYSTEM_SEED', 'SYSTEM')
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                var @event = new EmployeeProfileCreated
                {
                    ProfileId = profileId,
                    EmployeeId = employeeId,
                    PartTimeFraction = DefaultPartTimeFraction,
                    Position = null,
                    EffectiveFrom = new DateOnly(1, 1, 1),
                    ActorId = "SYSTEM_SEED",
                    ActorRole = "SYSTEM",
                    CorrelationId = null,
                };
                await outbox.EnqueueAsync(conn, tx, $"employee-profile-{employeeId}", @event, ct);

                await tx.CommitAsync(ct);
                seeded++;
            }
            catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "EmployeeProfile seed for {EmployeeId} lost concurrent-startup race (23505 on idx_employee_profiles_live); skipping",
                    employeeId);
                skippedRace++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed employee_profile for user {EmployeeId} — rolling back this row", employeeId);
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        logger.LogInformation(
            "Employee profile seeding complete — {Seeded} rows inserted, {SkippedRace} skipped (concurrent-startup race)",
            seeded, skippedRace);
    }
}
