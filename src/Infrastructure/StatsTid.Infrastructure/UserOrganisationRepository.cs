using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S148 C-13 (ADR-040 D4; QUAL-206) — the writer of a user's dated organisation history,
/// <c>user_organisations</c>, in the <c>user_agreement_codes</c> shape: one row per stretch of time,
/// end-exclusive <c>[effective_from, effective_to)</c> (ADR-018 D9), a per-row <c>version</c>, one open
/// row per user (<c>idx_user_organisations_live</c>) and one row per start date
/// (<c>idx_user_organisations_history</c>).
///
/// <para><b>What it is for.</b> Payroll and the screens ask "which institution was this person in on
/// that day?" (the institution's local profile reaches pay, ADR-043 D12), and
/// <see cref="EmploymentProfileResolver"/> answers it from this table. <c>users.primary_org_id</c> is
/// unchanged as the access and login-token anchor (ADR-038 D1) and is the cache of the row covering
/// today; no authorization decision reads this table.</para>
///
/// <para><b>Three methods, all <c>(conn, tx)</c> (ADR-018 D3).</b> Each write puts its row(s) AND its one
/// <c>user_organisations_audit</c> row in the caller's transaction (the
/// <c>WageTypeMappingRepository.AppendAuditAsync</c> precedent of a repository-owned audit row), so no
/// organisation write can exist without its audit row. The caller emits the outbox event in the same
/// transaction (it owns the outbox and the audit-projection mapper):
/// <list type="bullet">
///   <item><see cref="InsertFirstRowAsync"/> — the user's first row, from <c>0001-01-01</c>, version 1,
///     audit CREATED. The single write path for both callers that give a person their first row: the
///     user create and <see cref="UserOrganisationBackfillSeeder"/>.</item>
///   <item><see cref="LockTimelineAsync"/> — every row of the user's timeline, <c>FOR UPDATE</c>, open
///     row first then history ascending (the <c>UserAgreementCodeRepository</c> lock order).</item>
///   <item><see cref="TransferAsync"/> — the transfer's two shapes on the locked open row: a date after
///     its start closes it and opens a new row (audit SUPERSEDED); a date equal to its start replaces
///     its organisation in place (audit UPDATED).</item>
/// </list></para>
///
/// <para><b>No read method.</b> The resolver reads the table inside its own query (one statement, so the
/// profile and the organisation come from one snapshot), and payroll reads it through its own reader.
/// The class holds no state; it is a DI singleton so the handlers take it as a parameter like their
/// other repositories.</para>
/// </summary>
public sealed class UserOrganisationRepository
{
    /// <summary>
    /// Writes the user's FIRST <c>user_organisations</c> row — <paramref name="orgId"/> from
    /// <c>0001-01-01</c>, open, version 1 — and its audit row (CREATED, <c>previous_data</c> NULL,
    /// <c>new_data</c> <c>{ userId, orgId, effectiveFrom }</c>, versions NULL → 1). A unique violation
    /// (23505: the user already has an open row or a row from that date) propagates as a
    /// <see cref="PostgresException"/>; the seeder treats it as a lost startup race.
    ///
    /// <para><b>Why <c>0001-01-01</c>.</b> It is the first day of history, so every day a profile row can
    /// ever cover has an organisation (NR-P7): HR may backdate a new person's profile and agreement code
    /// to their real first day, before the create. The date is the SQL literal
    /// <c>DATE '0001-01-01'</c>, never a <see cref="DateOnly"/> parameter: Npgsql 8 maps
    /// <see cref="DateOnly.MinValue"/> to <c>DATE '-infinity'</c>, which would leave the row disagreeing
    /// with its audit row and event (both say <c>0001-01-01</c>) and with the test fixtures' rows (the
    /// reasoning at <c>EmployeeProfileRepository.CreateAsync</c>'s date binding). Read back through
    /// Npgsql, the stored date is <see cref="DateOnly.MinValue"/>.</para>
    /// </summary>
    /// <returns>The written row, read back by <c>RETURNING</c>; the caller's event takes its values from
    /// it, so the row and the event cannot disagree.</returns>
    public async Task<UserOrganisationRow> InsertFirstRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, string orgId, string actorId, string actorRole,
        CancellationToken ct = default)
    {
        UserOrganisationRow row;
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_organisations (
                assignment_id, user_id, org_id,
                effective_from, effective_to, version)
            VALUES (
                @assignmentId, @userId, @orgId,
                DATE '0001-01-01', NULL, 1)
            RETURNING assignment_id, org_id, effective_from, effective_to, version
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("assignmentId", Guid.NewGuid());
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("orgId", orgId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Defence in depth: INSERT ... RETURNING yields one row on success.
                throw new InvalidOperationException(
                    $"InsertFirstRowAsync produced no row for user_id='{userId}'.");
            }
            row = ReadRow(reader);
        }

        var newData = JsonSerializer.Serialize(new
        {
            userId,
            orgId = row.OrgId,
            effectiveFrom = FormatDate(row.EffectiveFrom),
        });
        await InsertAuditAsync(
            conn, tx, row.AssignmentId, userId, "CREATED",
            previousData: null, newData, versionBefore: null, versionAfter: row.Version,
            actorId, actorRole, ct);
        return row;
    }

    /// <summary>
    /// Locks EVERY row of the user's organisation timeline with <c>SELECT … FOR UPDATE</c>, open row
    /// first then history ascending (PostgreSQL locks in output order, so every writer shares one
    /// order). The caller must already hold the user's <c>users</c> row <c>FOR UPDATE</c>: both writers
    /// of an existing user's dated rows (the user edit and the dedicated agreement-code route) take it
    /// first, so they never interleave on these rows. Returns an empty list when the user has no row.
    /// </summary>
    public async Task<IReadOnlyList<UserOrganisationRow>> LockTimelineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT assignment_id, org_id, effective_from, effective_to, version
            FROM user_organisations
            WHERE user_id = @userId
            ORDER BY (effective_to IS NULL) DESC, effective_from
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        var rows = new List<UserOrganisationRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(ReadRow(reader));
        return rows;
    }

    /// <summary>
    /// The transfer's write on the user's locked OPEN row <paramref name="live"/> (from
    /// <see cref="LockTimelineAsync"/>, same transaction), dated <paramref name="transferDate"/>. The
    /// shape is decided by the date alone:
    /// <list type="bullet">
    ///   <item><b>equal to the open row's start</b> — in place: the row's <c>org_id</c> becomes
    ///     <paramref name="newOrgId"/>, <c>version</c> + 1, <c>updated_at</c> now; audit UPDATED, the
    ///     row before → the row after. (A second transfer on one day, or a transfer on the day of an
    ///     earlier one, would otherwise collide with the history index.)</item>
    ///   <item><b>after it</b> — close and open: the open row ends at <paramref name="transferDate"/>
    ///     (its version unchanged: a close is lifecycle, not a content edit) and a new open row starts
    ///     there at <c>live.Version + 1</c> (the agreement-code Case C rule); ONE audit row,
    ///     SUPERSEDED, on the new row, <c>previous_data</c> the closed row as it was before.</item>
    /// </list>
    /// The caller refuses a date before the open row's start (it would rewrite history this writer
    /// cannot represent); here such a date, or a row that is not open, is a programming error.
    /// </summary>
    public async Task<UserOrganisationTransferResult> TransferAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, UserOrganisationRow live, string newOrgId, DateOnly transferDate,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        if (live.EffectiveTo is not null || transferDate < live.EffectiveFrom)
            throw new InvalidOperationException(
                $"TransferAsync for user_id='{userId}' needs the open row and a date on or after its start.");

        var previousData = Snapshot(userId, live);

        if (transferDate == live.EffectiveFrom)
        {
            UserOrganisationRow updated;
            await using (var cmd = new NpgsqlCommand(
                """
                UPDATE user_organisations SET
                    org_id = @orgId,
                    version = version + 1,
                    updated_at = NOW()
                WHERE assignment_id = @assignmentId
                RETURNING assignment_id, org_id, effective_from, effective_to, version
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("assignmentId", live.AssignmentId);
                cmd.Parameters.AddWithValue("orgId", newOrgId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    // Defence in depth: unreachable while the timeline lock is held.
                    throw new InvalidOperationException(
                        $"TransferAsync matched no row for assignment_id='{live.AssignmentId}'; FOR UPDATE invariant violated.");
                }
                updated = ReadRow(reader);
            }

            await InsertAuditAsync(
                conn, tx, updated.AssignmentId, userId, "UPDATED",
                previousData, Snapshot(userId, updated), live.Version, updated.Version,
                actorId, actorRole, ct);
            return new UserOrganisationTransferResult(
                updated.AssignmentId, PredecessorAssignmentId: null, InPlace: true, updated.Version);
        }

        await using (var closeCmd = new NpgsqlCommand(
            """
            UPDATE user_organisations SET
                effective_to = @closeDate,
                updated_at = NOW()
            WHERE assignment_id = @assignmentId
            """, conn, tx))
        {
            closeCmd.Parameters.AddWithValue("closeDate", transferDate);
            closeCmd.Parameters.AddWithValue("assignmentId", live.AssignmentId);
            await closeCmd.ExecuteNonQueryAsync(ct);
        }

        UserOrganisationRow successor;
        await using (var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO user_organisations (
                assignment_id, user_id, org_id,
                effective_from, effective_to, version)
            VALUES (
                @assignmentId, @userId, @orgId,
                @effectiveFrom, NULL, @version)
            RETURNING assignment_id, org_id, effective_from, effective_to, version
            """, conn, tx))
        {
            insertCmd.Parameters.AddWithValue("assignmentId", Guid.NewGuid());
            insertCmd.Parameters.AddWithValue("userId", userId);
            insertCmd.Parameters.AddWithValue("orgId", newOrgId);
            insertCmd.Parameters.AddWithValue("effectiveFrom", transferDate);
            insertCmd.Parameters.AddWithValue("version", live.Version + 1);
            await using var reader = await insertCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Defence in depth: INSERT ... RETURNING yields one row on success.
                throw new InvalidOperationException(
                    $"TransferAsync produced no successor row for user_id='{userId}'.");
            }
            successor = ReadRow(reader);
        }

        await InsertAuditAsync(
            conn, tx, successor.AssignmentId, userId, "SUPERSEDED",
            previousData, Snapshot(userId, successor), live.Version, successor.Version,
            actorId, actorRole, ct);
        return new UserOrganisationTransferResult(
            successor.AssignmentId, PredecessorAssignmentId: live.AssignmentId, InPlace: false, successor.Version);
    }

    private static UserOrganisationRow ReadRow(NpgsqlDataReader reader) => new(
        AssignmentId: reader.GetGuid(0),
        OrgId: reader.GetString(1),
        EffectiveFrom: reader.GetFieldValue<DateOnly>(2),
        EffectiveTo: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
        Version: reader.GetInt64(4));

    private static string Snapshot(string userId, UserOrganisationRow row) => JsonSerializer.Serialize(new
    {
        userId,
        orgId = row.OrgId,
        effectiveFrom = FormatDate(row.EffectiveFrom),
        effectiveTo = row.EffectiveTo is { } to ? FormatDate(to) : null,
    });

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task InsertAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid assignmentId, string userId, string action,
        string? previousData, string newData, long? versionBefore, long versionAfter,
        string actorId, string actorRole, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_organisations_audit (
                assignment_id, user_id, action,
                previous_data, new_data,
                version_before, version_after,
                actor_id, actor_role)
            VALUES (
                @assignmentId, @userId, @action,
                @previousData::jsonb, @newData::jsonb,
                @versionBefore, @versionAfter,
                @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("assignmentId", assignmentId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.Add(new NpgsqlParameter("previousData", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)previousData ?? DBNull.Value,
        });
        cmd.Parameters.AddWithValue("newData", newData);
        cmd.Parameters.Add(new NpgsqlParameter("versionBefore", NpgsqlTypes.NpgsqlDbType.Bigint)
        {
            Value = (object?)versionBefore ?? DBNull.Value,
        });
        cmd.Parameters.AddWithValue("versionAfter", versionAfter);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// S148 C-13 — one <c>user_organisations</c> row as read under the caller's lock (or read back after a
/// write). <see cref="EffectiveTo"/> null = the open row.
/// </summary>
public sealed record UserOrganisationRow(
    Guid AssignmentId,
    string OrgId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version);

/// <summary>
/// S148 C-13 — what <see cref="UserOrganisationRepository.TransferAsync"/> wrote, for the
/// <c>UserOrganisationTransferred</c> event: the row that now holds the new organisation, the closed
/// row (null in place), the shape, and that row's version after the write.
/// </summary>
public sealed record UserOrganisationTransferResult(
    Guid AssignmentId,
    Guid? PredecessorAssignmentId,
    bool InPlace,
    long VersionAfter);
