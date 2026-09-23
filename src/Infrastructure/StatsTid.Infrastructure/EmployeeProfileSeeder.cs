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

    /// <summary>
    /// The backfill anchor — the earliest representable date, which is also the schema DEFAULT for
    /// <c>employee_profiles.effective_from</c>.
    ///
    /// <para>
    /// <b>Why a backfill must NOT be stamped "today"</b> (S33 Step 7a P1; preserved verbatim in
    /// intent by S143 / TASK-14308 when this seeder moved onto the shared write path). These rows
    /// describe employees who already existed, so their HISTORICAL periods have to resolve. The
    /// profile resolver selects the row whose <c>effective_from &lt;= asOfDate</c>; a row stamped
    /// today covers nothing before today, so every pre-deployment calculation finds no profile, and
    /// PCS/Compliance fail closed with a 500. Anchoring at <c>0001-01-01</c> means "as far back as
    /// anyone can ask", which is the truthful statement for a backfilled row: we do not know when
    /// this profile began, only that it was already in force.
    /// </para>
    ///
    /// <para>
    /// One constant, referenced by BOTH the row write and the <see cref="EmployeeProfileCreated"/>
    /// event below, so row/event date parity (ADR-018 D3) holds by construction rather than by two
    /// matching literals.
    /// </para>
    ///
    /// <para>
    /// <b>This value is <c>DateOnly.MinValue</c>, which Npgsql special-cases.</b> Npgsql 8 maps it to
    /// Postgres <c>DATE '-infinity'</c> by default, so it must NOT be bound as a <c>DateOnly</c>
    /// parameter or the column stops holding the finite date it held before.
    /// <see cref="EmployeeProfileRepository.CreateAsync"/> binds it as text with an explicit
    /// <c>::date</c> cast for exactly this reason — the comment at that binding carries the full
    /// account, including why the global opt-out is not the answer.
    /// </para>
    /// </summary>
    private static readonly DateOnly BackfillAnchor = new(1, 1, 1);

    /// <param name="profileRepository">
    /// S143 / TASK-14308 (QUAL-177, owner ruling OQ-4) — the seeder no longer writes its own INSERT
    /// statement; it calls <see cref="EmployeeProfileRepository.CreateAsync"/>, the single write path
    /// shared by the two create-a-person routes (this seeder and the admin create-person endpoint),
    /// passing <see cref="BackfillAnchor"/> as the effective date. The
    /// repository is injected rather than constructed here so this seeder shares the one DI-registered
    /// instance (and therefore the one configured <c>TimeProvider</c>) with every other caller.
    /// </param>
    public static async Task SeedAsync(
        DbConnectionFactory dbFactory,
        IOutboxEnqueue outbox,
        EmployeeProfileRepository profileRepository,
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
                // S33 Step 7a P1 absorption: backfill MUST use the '0001-01-01' anchor
                // (NOT today) so existing employees' historical periods resolve via the
                // resolver's `effective_from <= asOfDate` predicate. Stamping today on
                // backfill would leave pre-deployment periods uncovered → resolver returns
                // null → PCS/Compliance fail-closed with 500 on any historical calc.
                // Same-day PUT against a seeder-backfilled row routes to Case C
                // (effective_from='0001-01-01' < today), but the Step 7a P1 fix in
                // EmployeeProfileRepository.InsertLiveRowAsync now stamps
                // supersedingVersion = predecessor.Version + 1, so the ETag monotonicity
                // contract holds across the supersession.
                //
                // S143 / TASK-14308 (QUAL-177, owner ruling OQ-4) — THIS USED TO BE AN INLINE
                // INSERT. There were three ways to write an employee_profiles row and the one the
                // tests described (EmployeeProfileRepository.CreateAsync) had no production caller,
                // so it could drift from the two real paths forever while its tests stayed green.
                // The row written here is byte-identical to the pre-S143 one: the columns this
                // seeder used to leave to schema DEFAULTS (effective_from '0001-01-01', effective_to
                // NULL, version 1) are now written EXPLICITLY by CreateAsync with the same values,
                // and the anchor is passed rather than defaulted precisely so the difference from
                // the admin path's today-stamp is stated at the call site instead of hidden in a
                // parameter default.
                // S137 / ADR-040 D4 — employment_category is populated same-tx from the
                // users value via the scalar subselect inside CreateAsync (the seeder iterates
                // user_ids read from users, so the row exists). dated==live is the S137 invariant;
                // users' category is write-once until Increment 3, so copy-from-users ==
                // copy-from-predecessor by construction.
                //
                // Transaction shape UNCHANGED (ADR-018 D5): CreateAsync is a (conn, tx) overload,
                // so the row INSERT, the audit row and the outbox event below still commit as one
                // atomic per-row unit, and the 23505 a lost startup race raises still propagates to
                // this loop's catch.
                var (profileId, profileVersion) = await profileRepository.CreateAsync(
                    conn, tx,
                    new EmployeeProfileCreateRequest(
                        EmployeeId: employeeId,
                        PartTimeFraction: DefaultPartTimeFraction,
                        Position: null,
                        EffectiveFrom: BackfillAnchor),
                    ct);

                // Step 7a P2 fix — emit a CREATED audit row in the same per-row tx
                // so the largest migration scenario this sprint introduces (backfill
                // of all existing users) doesn't leave the audit table empty. Mirrors
                // the UPDATED audit shape at EmployeeProfileEndpoints.cs PUT path.
                // previous_data is NULL (no predecessor), version_before is NULL,
                // version_after = the version CreateAsync actually wrote (1), actor_id =
                // SYSTEM_SEED (matches the event's ActorId so audit + outbox cross-reference
                // cleanly).
                // S143 / TASK-14308 — version_after was the literal `1`. It is now taken from the
                // party that performed the write, the one-authority-per-fact rule this codebase
                // already applies to `createdUsersVersion` in AdminEndpoints. The value is
                // unchanged; what changes is that it can no longer disagree with the row.
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
                        NULL, @versionAfter,
                        'SYSTEM_SEED', 'SYSTEM')
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionAfter", profileVersion);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // S143 / TASK-14308 — the event's EffectiveFrom is the SAME constant the row was
                // stamped with (it was an independently-written `new DateOnly(1, 1, 1)` literal).
                // Row/event date parity, ADR-018 D3, now by construction rather than by inspection.
                var @event = new EmployeeProfileCreated
                {
                    ProfileId = profileId,
                    EmployeeId = employeeId,
                    PartTimeFraction = DefaultPartTimeFraction,
                    Position = null,
                    EffectiveFrom = BackfillAnchor,
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
