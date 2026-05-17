using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S34 / TASK-3403 — Phase 4e (ADR-023 D2 option (b)) bootstrap backfill that seeds the
/// new <c>user_agreement_codes</c> versioned-history table with one live row per existing
/// user on first boot. Reads each user's current <c>users.agreement_code</c> scalar (the
/// pre-S34 source of truth) and inserts a matching history row carrying that value at
/// <c>effective_from = '0001-01-01'</c>. Mirrors the S31 / TASK-3106
/// <see cref="EmployeeProfileSeeder"/> shape — most of the structure carries over verbatim;
/// the differences are documented inline below.
///
/// <para>
/// <b>Idempotent.</b> Reads <c>users</c> + existing live <c>user_agreement_codes</c> rows,
/// inserts ONLY for users that don't yet have a live (<c>effective_to IS NULL</c>) row.
/// Each new row commits atomically with a <see cref="UserAgreementCodeSeeded"/> outbox
/// event in a single per-row transaction (ADR-018 D3 atomic outbox) — matching the
/// 3-way atomicity contract that <c>POST /api/admin/users</c> uses for net-new users in
/// steady state (TASK-3407, separate task).
/// </para>
///
/// <para>
/// <b>History-covering default — <c>effective_from = '0001-01-01'</c>.</b> Per the S33
/// Step 7a cycle 1 absorption (also restated in TASK-3403's binding behavior), backfill
/// MUST use the schema DEFAULT <c>'0001-01-01'</c> — NOT today. Past-period readers
/// (PCS planner snapshot resolution, payroll export effective-date lookup) route through
/// <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/>, which selects the row
/// whose history window <c>[effective_from, effective_to)</c> contains the asOfDate. If
/// we stamped today, every pre-deployment period would resolve to <c>null</c> and the
/// resolver would fail-closed on any historical calc — defeating the very replay-stability
/// fix this sprint is meant to ship. Same-day PUTs against a seeder-backfilled row route
/// to Case C (Supersede) in <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>
/// (<c>effective_from='0001-01-01' &lt; today</c>), where the successor inherits
/// <c>predecessor.Version + 1</c> per the S33 Step 7a P1 ETag-monotonicity refinement so
/// concurrent admin edits surface a 412 instead of silently overwriting.
/// </para>
///
/// <para>
/// <b>Concurrent-startup 23505 race handled inline.</b> Two app instances booting
/// simultaneously can both pass the NOT-EXISTS predicate for the same user and race
/// to INSERT — the partial-unique-index <c>idx_user_agreement_codes_live</c> aborts the
/// second INSERT with <c>PostgresException(SqlState = "23505")</c>. We catch that
/// specific code and skip-without-fail (logs a warning + continues with the next user)
/// because the row the loser would have inserted is, by construction, semantically
/// identical to the row the winner did insert (same <c>user_id</c>, same
/// <c>agreement_code</c> sourced from <c>users.agreement_code</c>, same
/// <c>effective_from='0001-01-01'</c>). This is the SAME defect class that the S31
/// <see cref="EmployeeProfileSeeder"/> deferred to Phase 4e (candidate #2 — the
/// EmployeeProfileSeeder race); we ship the fix INLINE here so the same defect does
/// not carry through S34.
/// </para>
///
/// <para>
/// <b>Audit row + outbox event in the same per-row tx.</b> Each successful seed insert
/// commits 3 writes atomically: (1) <c>user_agreement_codes</c> live row at
/// <c>(effective_from='0001-01-01', effective_to=NULL, version=1)</c>; (2)
/// <c>user_agreement_codes_audit</c> row with <c>action='CREATED'</c>,
/// <c>version_before=NULL</c>, <c>version_after=1</c>, <c>actor_id='SYSTEM_SEED'</c>,
/// <c>actor_role='SYSTEM'</c> (matches the S31 EmployeeProfileSeeder actor stamp
/// convention so audit + outbox cross-reference cleanly); (3) outbox
/// <see cref="UserAgreementCodeSeeded"/> event on the canonical
/// <c>user-{userId}</c> stream (same stream as <see cref="UserCreated"/> and
/// <see cref="UserAgreementCodeChanged"/> per TASK-3309 — replay determinism on a
/// single per-user lineage).
/// </para>
///
/// <para>
/// <b>Why not <see cref="UserAgreementCodeChanged"/>?</b> Per the Step 0b BLOCKER 1
/// absorption documented on <see cref="UserAgreementCodeSeeded"/>, the Seeded shape
/// carries NO predecessor (no <c>OldAgreementCode</c>) — Changed always carries both
/// old and new. Keeping the two distinct makes the Phase 4e replay-data trail walkable
/// without nullable-old-value parsing. Seeded is the distinct origin marker for the
/// FIRST-EVER assignment to a user; Changed is for admin same-day mutations
/// (TASK-3309). Different from the S33 cycle 2 absorption — that one reconciled the
/// PUT path; this one fixes the bootstrap path.
/// </para>
///
/// <para>
/// <b>Seeder route (vs. SQL-side INSERTs in init.sql)</b> chosen for the same reason
/// EmployeeProfileSeeder was: event emission requires <see cref="IOutboxEnqueue"/>
/// serialization — SQL-side INSERTs into <c>outbox_events</c> would bypass the
/// EventSerializer registry and break replay determinism.
/// </para>
/// </summary>
public static class UserAgreementCodeBackfillSeeder
{
    public static async Task SeedAsync(
        DbConnectionFactory dbFactory,
        IOutboxEnqueue outbox,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);

        // Find active users that lack a live user_agreement_codes row, and read
        // their current users.agreement_code scalar in the same query so we can
        // copy it forward into the new history row. is_active=TRUE filter mirrors
        // the S31 EmployeeProfileSeeder convention — deactivated users don't need
        // history rows yet (and admins can reactivate via the steady-state PUT
        // path which goes through SupersedeAndCreateAsync).
        await using var findMissingCmd = new NpgsqlCommand(
            """
            SELECT u.user_id, u.agreement_code
            FROM users u
            WHERE u.is_active = TRUE
              AND NOT EXISTS (
                  SELECT 1 FROM user_agreement_codes uac
                  WHERE uac.user_id = u.user_id AND uac.effective_to IS NULL
              )
            ORDER BY u.user_id
            """, conn);

        var missing = new List<(string UserId, string AgreementCode)>();
        await using (var reader = await findMissingCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                missing.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        if (missing.Count == 0)
        {
            logger.LogDebug("User agreement codes already seeded for all active users — skipping");
            return;
        }

        logger.LogInformation("Seeding user_agreement_codes for {Count} users without live rows...", missing.Count);

        var seeded = 0;
        var skippedRace = 0;
        foreach (var (userId, agreementCode) in missing)
        {
            // Each seed insert rides its own atomic tx (row INSERT + audit row +
            // outbox event in one transaction; ADR-018 D3 atomic outbox pattern).
            // Independent transactions per row keep retry semantics clean if any
            // single insert fails — and isolate the 23505 race-loss path below
            // to a single user without taking down the whole backfill.
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // History-covering default — see class XML doc. The schema DEFAULT
                // is also '0001-01-01' but we pass it explicitly here so the
                // contract is visible at the call site (not just in init.sql).
                var effectiveFrom = new DateOnly(1, 1, 1);
                var assignmentId = Guid.NewGuid();

                await using (var insertCmd = new NpgsqlCommand(
                    """
                    INSERT INTO user_agreement_codes
                        (assignment_id, user_id, agreement_code,
                         effective_from, effective_to, version)
                    VALUES
                        (@assignmentId, @userId, @agreementCode,
                         @effectiveFrom, NULL, 1)
                    """, conn, tx))
                {
                    insertCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                    insertCmd.Parameters.AddWithValue("userId", userId);
                    insertCmd.Parameters.AddWithValue("agreementCode", agreementCode);
                    insertCmd.Parameters.AddWithValue("effectiveFrom", effectiveFrom);
                    await insertCmd.ExecuteNonQueryAsync(ct);
                }

                // Audit CREATED row in the same per-row tx — mirrors the S31
                // EmployeeProfileSeeder Step 7a P2 fix shape so the largest
                // migration scenario this sprint introduces (backfill of every
                // existing user) doesn't leave the audit table empty.
                // previous_data is NULL (no predecessor), version_before is NULL,
                // version_after = 1, actor_id = SYSTEM_SEED (matches the event's
                // ActorId so audit + outbox cross-reference cleanly).
                var newData = JsonSerializer.Serialize(new
                {
                    userId,
                    agreementCode,
                    effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO user_agreement_codes_audit (
                        assignment_id, user_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @assignmentId, @userId, 'CREATED',
                        NULL, @newData::jsonb,
                        NULL, 1,
                        'SYSTEM_SEED', 'SYSTEM')
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                    auditCmd.Parameters.AddWithValue("userId", userId);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // Outbox event — UserAgreementCodeSeeded carries NO OldAgreementCode
                // (no predecessor; this is the FIRST-EVER row for the user). Same
                // canonical user-{userId} stream as UserCreated + UserAgreementCodeChanged
                // (TASK-3309) so the per-user lineage replays in one walk.
                var @event = new UserAgreementCodeSeeded
                {
                    UserId = userId,
                    AgreementCode = agreementCode,
                    EffectiveFrom = effectiveFrom,
                    RowVersion = 1L,
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
                // Concurrent-startup race: another app instance won the INSERT for
                // the same user_id under the partial-unique-index
                // idx_user_agreement_codes_live. The winner's row is, by construction,
                // semantically identical (same source: users.agreement_code) so we
                // skip-without-fail and continue. Ships the fix inline that S31
                // EmployeeProfileSeeder deferred to Phase 4e (candidate #2).
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "User agreement-code backfill for user_id='{UserId}' lost a concurrent-startup race (PostgresException 23505 on idx_user_agreement_codes_live); another instance inserted the live row first — skipping",
                    userId);
                skippedRace++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed user_agreement_codes for user {UserId} — rolling back this row", userId);
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        logger.LogInformation(
            "User agreement-code seeding complete — {Seeded} rows inserted with UserAgreementCodeSeeded events; {SkippedRace} rows skipped due to concurrent-startup 23505 race",
            seeded, skippedRace);
    }
}
