using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Big-bang cutover migrator that rewrites pre-S21 <c>local_configurations</c> rows into
/// the typed <c>local_agreement_profiles</c> shape per ADR-017 D4 and the SPRINT-21
/// "Migration Plan (Deliverable #3) > Per-tuple migration logic (Step 2)".
///
/// <para>
/// Algorithm (single transaction):
/// <list type="number">
///   <item><description>Discover unique <c>(org_id, agreement_code, ok_version)</c> tuples
///   that have at least one currently-effective <c>is_active = TRUE</c> row.</description></item>
///   <item><description>For each tuple, partition rows by <c>config_key</c>:
///     <list type="bullet">
///       <item><description><b>Overridable keys</b> (in <see cref="LocalAgreementProfileMetadata.LegacyKeyToColumn"/>):
///         pick the most-recently-effective row, buffer its parsed value into the
///         future profile row, and emit <c>DROPPED_DUPLICATE_AT_MIGRATION</c> audit
///         rows for losers.</description></item>
///       <item><description><b>Informational keys</b> (in <see cref="LocalAgreementProfileMetadata.LegacyInformationalKeys"/>):
///         emit <c>DROPPED_INFORMATIONAL</c> for every row.</description></item>
///       <item><description><b>Unknown keys</b>: emit <c>DROPPED_UNKNOWN_KEY</c> for every row.</description></item>
///     </list>
///   </description></item>
///   <item><description>If at least one overridable key was buffered, INSERT a new
///   <c>local_agreement_profiles</c> row with <c>effective_from = MIN(picked rows' effective_from)</c>
///   and <c>effective_to = NULL</c>; emit a <c>MIGRATED_FROM_LEGACY</c> audit row in
///   <c>local_agreement_profile_audit</c> capturing the per-profile delta.</description></item>
///   <item><description>Run validation assertions before COMMIT:
///     profile count, drop count, partial-unique-index check, no all-NULL profiles.
///     Any assertion failure throws <see cref="MigrationValidationException"/>; the
///     transaction rolls back.</description></item>
/// </list>
/// </para>
///
/// <para>Idempotency: per the TASK-2106 spec, if any <c>local_agreement_profiles</c> row
/// already exists for a tuple before the migration, the migration skips that tuple
/// (logs a warning, no merge attempt). For S21 this is a development convenience;
/// production runs once.</para>
///
/// <para>Audit actor: all migration-emitted audit rows use <c>actor_id = 'system'</c>
/// and <c>actor_role = 'GlobalAdmin'</c>.</para>
/// </summary>
public sealed class LocalAgreementProfileMigrator
{
    private const string SystemActorId = "system";
    private const string SystemActorRole = "GlobalAdmin";

    private readonly DbConnectionFactory _connectionFactory;
    private readonly ILogger<LocalAgreementProfileMigrator> _logger;

    public LocalAgreementProfileMigrator(
        DbConnectionFactory connectionFactory,
        ILogger<LocalAgreementProfileMigrator> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs the big-bang migration in a single transaction. Returns aggregate counts
    /// per category. On any validation-assertion failure, throws
    /// <see cref="MigrationValidationException"/> after rolling back the transaction.
    /// </summary>
    public async Task<MigrationResult> RebuildAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // Serializable: the migration is intended to run with the API offline, so we lean
        // on the strongest isolation to defend against any incidental reader/writer.
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var result = await RunMigrationAsync(conn, tx, ct);
            await ValidatePostMigrationAsync(conn, tx, result, ct);
            await tx.CommitAsync(ct);
            _logger.LogInformation(
                "LocalAgreementProfileMigrator completed: {ProfilesCreated} profiles created, " +
                "{RowsMigrated} legacy rows absorbed, {DroppedDuplicates} duplicates / " +
                "{DroppedInformational} informational / {DroppedUnknown} unknown rows dropped, " +
                "{TuplesSkipped} tuples skipped (idempotency).",
                result.ProfilesCreated, result.RowsMigrated,
                result.RowsDroppedDuplicates, result.RowsDroppedInformational, result.RowsDroppedUnknown,
                result.TuplesSkipped);
            return result;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "Rollback after migration failure threw; original exception will propagate.");
            }
            throw;
        }
    }

    // -------------------------------------------------------------------
    // Step 2: Per-tuple migration loop
    // -------------------------------------------------------------------
    private async Task<MigrationResult> RunMigrationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        var tuples = await DiscoverEligibleTuplesAsync(conn, tx, ct);
        _logger.LogInformation(
            "LocalAgreementProfileMigrator discovered {TupleCount} eligible (org_id, agreement_code, ok_version) tuples.",
            tuples.Count);

        int profilesCreated = 0;
        int rowsMigrated = 0;
        int rowsDroppedDuplicates = 0;
        int rowsDroppedInformational = 0;
        int rowsDroppedUnknown = 0;
        int tuplesSkipped = 0;

        foreach (var tuple in tuples)
        {
            // Idempotency guard: if any row exists for this tuple in the new table, skip.
            if (await ProfileExistsForTupleAsync(conn, tx, tuple, ct))
            {
                _logger.LogWarning(
                    "Tuple ({OrgId}, {AgreementCode}, {OkVersion}) already has local_agreement_profiles row(s); " +
                    "skipping (idempotency). Existing data is left untouched.",
                    tuple.OrgId, tuple.AgreementCode, tuple.OkVersion);
                tuplesSkipped++;
                continue;
            }

            var rows = await LoadEligibleRowsForTupleAsync(conn, tx, tuple, ct);
            if (rows.Count == 0)
            {
                // Defensive: should not happen given the discovery query, but skip cleanly.
                continue;
            }

            // Partition by config_key; classify; emit per-row audit; buffer winners.
            var profileColumns = new Dictionary<string, object>(StringComparer.Ordinal);
            DateOnly? earliestEffectiveFrom = null;
            int absorbedCount = 0;

            foreach (var keyGroup in rows
                .GroupBy(r => r.ConfigKey, StringComparer.Ordinal))
            {
                var configKey = keyGroup.Key;
                // Most-recently-effective first; pre-sort here so 'losers' = group.Skip(1).
                var ordered = keyGroup
                    .OrderByDescending(r => r.EffectiveFrom)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToList();

                if (LocalAgreementProfileMetadata.LegacyKeyToColumn.TryGetValue(configKey, out var columnName))
                {
                    // Cycle-1 review BLOCKER fix: walk the ordered rows newest-first and pick
                    // the first one whose value parses. Earlier behavior collapsed the entire
                    // key group to DROPPED_UNKNOWN_KEY when only the newest row was unparseable
                    // — silent loss of recoverable older overrides. Now: rows newer than the
                    // chosen winner that fail to parse are emitted as DROPPED_UNKNOWN_KEY (the
                    // bad data the admin most recently entered); rows older than the winner
                    // are emitted as DROPPED_DUPLICATE_AT_MIGRATION (superseded). If NO row
                    // in the group parses, all rows are emitted as DROPPED_UNKNOWN_KEY and
                    // the column is not populated — same as the original behavior for the
                    // truly-unrecoverable case.
                    int winnerIndex = -1;
                    object? parsedWinnerValue = null;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (TryParseConfigValue(configKey, ordered[i].ConfigValue, out var parsedValue, out _))
                        {
                            winnerIndex = i;
                            parsedWinnerValue = parsedValue;
                            break;
                        }
                    }

                    if (winnerIndex < 0)
                    {
                        // No parseable row in the group — drop all as unknown.
                        _logger.LogWarning(
                            "Tuple ({OrgId}, {AgreementCode}, {OkVersion}) key '{ConfigKey}' has no parseable rows; " +
                            "all {Count} rows recorded as DROPPED_UNKNOWN_KEY.",
                            tuple.OrgId, tuple.AgreementCode, tuple.OkVersion, configKey, ordered.Count);
                        foreach (var row in ordered)
                        {
                            await InsertLegacyAuditAsync(
                                conn, tx, row.ConfigId, "DROPPED_UNKNOWN_KEY",
                                previousValue: row.ConfigValue, newValue: null, ct);
                            rowsDroppedUnknown++;
                        }
                        continue;
                    }

                    var winner = ordered[winnerIndex];
                    if (winnerIndex > 0)
                    {
                        // Newest rows were unparseable; the admin entered bad data on top of a
                        // valid history. Recover the older valid value but record the bad rows.
                        _logger.LogWarning(
                            "Tuple ({OrgId}, {AgreementCode}, {OkVersion}) key '{ConfigKey}' newest {BadCount} " +
                            "row(s) had unparseable values; falling through to row {WinnerConfigId} (effective_from={EffectiveFrom}).",
                            tuple.OrgId, tuple.AgreementCode, tuple.OkVersion,
                            configKey, winnerIndex, winner.ConfigId, winner.EffectiveFrom);
                    }

                    profileColumns[columnName] = parsedWinnerValue!;
                    earliestEffectiveFrom = earliestEffectiveFrom is null
                        ? winner.EffectiveFrom
                        : (winner.EffectiveFrom < earliestEffectiveFrom.Value ? winner.EffectiveFrom : earliestEffectiveFrom);
                    absorbedCount++;

                    // Emit DROPPED_UNKNOWN_KEY for newer-than-winner unparseable rows;
                    // DROPPED_DUPLICATE_AT_MIGRATION for older-than-winner valid losers.
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (i == winnerIndex) continue;
                        var row = ordered[i];
                        var action = i < winnerIndex ? "DROPPED_UNKNOWN_KEY" : "DROPPED_DUPLICATE_AT_MIGRATION";
                        await InsertLegacyAuditAsync(
                            conn, tx, row.ConfigId, action,
                            previousValue: row.ConfigValue, newValue: null, ct);
                        if (action == "DROPPED_UNKNOWN_KEY") rowsDroppedUnknown++;
                        else rowsDroppedDuplicates++;
                    }
                }
                else if (LocalAgreementProfileMetadata.LegacyInformationalKeys.Contains(configKey))
                {
                    foreach (var row in ordered)
                    {
                        await InsertLegacyAuditAsync(
                            conn, tx, row.ConfigId, "DROPPED_INFORMATIONAL",
                            previousValue: row.ConfigValue, newValue: null, ct);
                        rowsDroppedInformational++;
                    }
                }
                else
                {
                    foreach (var row in ordered)
                    {
                        await InsertLegacyAuditAsync(
                            conn, tx, row.ConfigId, "DROPPED_UNKNOWN_KEY",
                            previousValue: row.ConfigValue, newValue: null, ct);
                        rowsDroppedUnknown++;
                    }
                }
            }

            // Only insert a profile row if at least one overridable key was buffered.
            if (profileColumns.Count > 0 && earliestEffectiveFrom is not null)
            {
                var newProfileId = Guid.NewGuid();
                await InsertProfileRowAsync(
                    conn, tx, newProfileId, tuple, earliestEffectiveFrom.Value, profileColumns, ct);
                await InsertProfileAuditAsync(
                    conn, tx, newProfileId, "MIGRATED_FROM_LEGACY",
                    BuildMigratedFromLegacyDelta(absorbedCount, profileColumns), ct);
                profilesCreated++;
                rowsMigrated += absorbedCount;
            }
        }

        return new MigrationResult(
            ProfilesCreated: profilesCreated,
            RowsMigrated: rowsMigrated,
            RowsDroppedDuplicates: rowsDroppedDuplicates,
            RowsDroppedInformational: rowsDroppedInformational,
            RowsDroppedUnknown: rowsDroppedUnknown,
            TuplesSkipped: tuplesSkipped);
    }

    // -------------------------------------------------------------------
    // Step 4: Post-migration validation assertions (still inside the tx)
    // -------------------------------------------------------------------
    private static async Task ValidatePostMigrationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, MigrationResult result, CancellationToken ct)
    {
        // Assertion 3: No partial-unique-index violations on open profiles.
        // (We check this first because a duplicate would silently bypass the
        // expected-counts checks if it's been there since before the migration.)
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM (
                SELECT org_id, agreement_code, ok_version, COUNT(*) AS open_count
                FROM local_agreement_profiles
                WHERE effective_to IS NULL
                GROUP BY org_id, agreement_code, ok_version
                HAVING COUNT(*) > 1
            ) violations
            """, conn, tx))
        {
            var violations = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            if (violations > 0)
            {
                throw new MigrationValidationException(
                    $"Partial-unique-index violation: {violations} (org_id, agreement_code, ok_version) " +
                    "tuple(s) have more than one open-ended profile row. Migration aborted.");
            }
        }

        // Assertion 4: No all-NULL profile rows. Every profile row created by this
        // migration MUST have at least one non-NULL overridable column.
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM local_agreement_profiles
            WHERE weekly_norm_hours IS NULL
              AND max_flex_balance IS NULL
              AND flex_carryover_max IS NULL
              AND max_overtime_hours_per_period IS NULL
              AND overtime_requires_pre_approval IS NULL
            """, conn, tx))
        {
            var allNullCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            if (allNullCount > 0)
            {
                throw new MigrationValidationException(
                    $"All-NULL profile rows detected: {allNullCount}. A profile row with no overridable values " +
                    "is equivalent to no profile and must not exist. Migration aborted.");
            }
        }

        // Assertion 1: profile-creation count exact match. The migration's own footprint is
        // (DISTINCT profile_id WHERE action='MIGRATED_FROM_LEGACY' AND actor_id='system').
        // Cycle-1 review WARNING: tightened from >= to == so a silent INSERT failure surfaces.
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(DISTINCT a.profile_id)
            FROM local_agreement_profile_audit a
            WHERE a.action = 'MIGRATED_FROM_LEGACY'
              AND a.actor_id = @actor
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("actor", SystemActorId);
            var profileAuditCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            if (profileAuditCount != result.ProfilesCreated)
            {
                throw new MigrationValidationException(
                    $"Profile-audit count mismatch: expected exactly {result.ProfilesCreated} " +
                    $"MIGRATED_FROM_LEGACY rows by actor='system', found {profileAuditCount}. Migration aborted.");
            }
        }

        // Assertion 2: drop-count exact match. The migration's own DROPPED_* emissions in
        // local_configuration_audit are filtered by actor_id='system' (no pre-S21 admin
        // wrote with that actor). The sum of in-memory counters must equal the DB count.
        var expectedDropTotal =
            result.RowsDroppedDuplicates +
            result.RowsDroppedInformational +
            result.RowsDroppedUnknown;
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM local_configuration_audit
            WHERE actor_id = @actor
              AND action IN ('DROPPED_DUPLICATE_AT_MIGRATION', 'DROPPED_INFORMATIONAL', 'DROPPED_UNKNOWN_KEY')
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("actor", SystemActorId);
            var dropAuditCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            if (dropAuditCount != expectedDropTotal)
            {
                throw new MigrationValidationException(
                    $"Drop-audit count mismatch: in-memory tally is {expectedDropTotal} " +
                    $"({result.RowsDroppedDuplicates} dup + {result.RowsDroppedInformational} info + " +
                    $"{result.RowsDroppedUnknown} unknown), DB has {dropAuditCount} rows by " +
                    $"actor='system' with DROPPED_* actions. Migration aborted.");
            }
        }
    }

    // -------------------------------------------------------------------
    // Tuple discovery & row loading
    // -------------------------------------------------------------------
    private static async Task<IReadOnlyList<TupleKey>> DiscoverEligibleTuplesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT org_id, agreement_code, ok_version
            FROM local_configurations
            WHERE is_active = TRUE
              AND effective_from <= CURRENT_DATE
              AND (effective_to IS NULL OR effective_to >= CURRENT_DATE)
            ORDER BY org_id, agreement_code, ok_version
            """, conn, tx);
        var tuples = new List<TupleKey>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tuples.Add(new TupleKey(
                OrgId: reader.GetString(0),
                AgreementCode: reader.GetString(1),
                OkVersion: reader.GetString(2)));
        }
        return tuples;
    }

    private static async Task<bool> ProfileExistsForTupleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, TupleKey tuple, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT 1 FROM local_agreement_profiles
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("orgId", tuple.OrgId);
        cmd.Parameters.AddWithValue("agreementCode", tuple.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", tuple.OkVersion);
        var found = await cmd.ExecuteScalarAsync(ct);
        return found is not null;
    }

    private static async Task<IReadOnlyList<LegacyRow>> LoadEligibleRowsForTupleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, TupleKey tuple, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT config_id, config_key, config_value, effective_from, created_at
            FROM local_configurations
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND is_active = TRUE
              AND effective_from <= CURRENT_DATE
              AND (effective_to IS NULL OR effective_to >= CURRENT_DATE)
            """, conn, tx);
        cmd.Parameters.AddWithValue("orgId", tuple.OrgId);
        cmd.Parameters.AddWithValue("agreementCode", tuple.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", tuple.OkVersion);
        var rows = new List<LegacyRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new LegacyRow(
                ConfigId: reader.GetGuid(0),
                ConfigKey: reader.GetString(1),
                ConfigValue: reader.GetString(2),
                EffectiveFrom: DateOnly.FromDateTime(reader.GetDateTime(3)),
                CreatedAt: reader.GetDateTime(4)));
        }
        return rows;
    }

    // -------------------------------------------------------------------
    // INSERT helpers — profile row, profile-audit row, legacy-audit row
    // -------------------------------------------------------------------
    private static async Task InsertProfileRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid profileId, TupleKey tuple, DateOnly effectiveFrom,
        IReadOnlyDictionary<string, object> profileColumns, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by, created_at)
            VALUES (
                @profileId, @orgId, @agreementCode, @okVersion,
                @effectiveFrom, NULL,
                @weeklyNormHours, @maxFlexBalance, @flexCarryoverMax,
                @maxOvertimeHoursPerPeriod, @overtimeRequiresPreApproval,
                @createdBy, @createdAt)
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", profileId);
        cmd.Parameters.AddWithValue("orgId", tuple.OrgId);
        cmd.Parameters.AddWithValue("agreementCode", tuple.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", tuple.OkVersion);
        cmd.Parameters.AddWithValue("effectiveFrom", effectiveFrom);
        cmd.Parameters.AddWithValue("weeklyNormHours", ColumnOrNull(profileColumns, "weekly_norm_hours"));
        cmd.Parameters.AddWithValue("maxFlexBalance", ColumnOrNull(profileColumns, "max_flex_balance"));
        cmd.Parameters.AddWithValue("flexCarryoverMax", ColumnOrNull(profileColumns, "flex_carryover_max"));
        cmd.Parameters.AddWithValue("maxOvertimeHoursPerPeriod", ColumnOrNull(profileColumns, "max_overtime_hours_per_period"));
        cmd.Parameters.AddWithValue("overtimeRequiresPreApproval", ColumnOrNull(profileColumns, "overtime_requires_pre_approval"));
        cmd.Parameters.AddWithValue("createdBy", SystemActorId);
        cmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertProfileAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid profileId, string action, string deltaJson, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profile_audit (profile_id, action, delta_jsonb, actor_id, actor_role)
            VALUES (@profileId, @action, @delta::jsonb, @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", profileId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("delta", deltaJson);
        cmd.Parameters.AddWithValue("actorId", SystemActorId);
        cmd.Parameters.AddWithValue("actorRole", SystemActorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertLegacyAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, string action, string? previousValue, string? newValue, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_configuration_audit (config_id, action, previous_value, new_value, actor_id, actor_role)
            VALUES (@configId, @action, @previousValue::jsonb, @newValue::jsonb, @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousValue", (object?)previousValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newValue", (object?)newValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", SystemActorId);
        cmd.Parameters.AddWithValue("actorRole", SystemActorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // -------------------------------------------------------------------
    // Value parsing & helpers
    // -------------------------------------------------------------------
    /// <summary>
    /// Parses the JSON-encoded <c>config_value</c> string into the type expected by the
    /// matching profile column. Mirrors <c>ConfigResolutionService.TryParseDecimal</c>:
    /// values may be raw numbers/booleans or JSON-quoted strings containing a number/bool.
    /// </summary>
    private static bool TryParseConfigValue(
        string configKey, string rawJson, out object? parsedValue, out string? error)
    {
        parsedValue = null;
        error = null;

        var trimmed = rawJson.Trim().Trim('"');
        if (string.Equals(configKey, "OvertimeRequiresPreApproval", StringComparison.Ordinal))
        {
            if (bool.TryParse(trimmed, out var boolVal))
            {
                parsedValue = boolVal;
                return true;
            }
            error = $"value '{rawJson}' is not a valid boolean";
            return false;
        }

        // All remaining overridable keys are decimals.
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var decVal))
        {
            parsedValue = decVal;
            return true;
        }

        error = $"value '{rawJson}' is not a valid decimal";
        return false;
    }

    private static object ColumnOrNull(IReadOnlyDictionary<string, object> profileColumns, string columnName) =>
        profileColumns.TryGetValue(columnName, out var value) ? value : DBNull.Value;

    /// <summary>
    /// Builds the JSONB delta for a <c>MIGRATED_FROM_LEGACY</c> profile-audit row.
    /// Hand-rolled JSON (no System.Text.Json import) keeps Infrastructure dependency
    /// surface narrow and matches the existing <c>InsertLegacyAuditAsync</c> raw-JSONB style.
    /// </summary>
    private static string BuildMigratedFromLegacyDelta(
        int migratedFromCount, IReadOnlyDictionary<string, object> profileColumns)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"migrated_from_count\":").Append(migratedFromCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"profile_columns\":{");
        bool first = true;
        foreach (var kvp in profileColumns)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kvp.Key).Append("\":");
            switch (kvp.Value)
            {
                case decimal d:
                    sb.Append(d.ToString(CultureInfo.InvariantCulture));
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                default:
                    // Defensive — unexpected type. Render as a JSON string.
                    sb.Append('"').Append(EscapeJsonString(kvp.Value?.ToString() ?? "")).Append('"');
                    break;
            }
        }
        sb.Append("}}");
        return sb.ToString();
    }

    private static string EscapeJsonString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // -------------------------------------------------------------------
    // Internal record types
    // -------------------------------------------------------------------
    private readonly record struct TupleKey(string OrgId, string AgreementCode, string OkVersion);

    private sealed record LegacyRow(
        Guid ConfigId,
        string ConfigKey,
        string ConfigValue,
        DateOnly EffectiveFrom,
        DateTime CreatedAt);
}

/// <summary>
/// Aggregate counts emitted by <see cref="LocalAgreementProfileMigrator.RebuildAsync"/>.
///
/// <list type="bullet">
///   <item><description><see cref="ProfilesCreated"/> — number of new
///   <c>local_agreement_profiles</c> rows the migration inserted.</description></item>
///   <item><description><see cref="RowsMigrated"/> — number of legacy rows whose values
///   were absorbed into a profile column (winners only).</description></item>
///   <item><description><see cref="RowsDroppedDuplicates"/> — losers in a multi-row
///   overridable-key collision.</description></item>
///   <item><description><see cref="RowsDroppedInformational"/> — rows for keys in
///   <see cref="LocalAgreementProfileMetadata.LegacyInformationalKeys"/>.</description></item>
///   <item><description><see cref="RowsDroppedUnknown"/> — rows for keys outside both the
///   overridable map and the informational set (typos, deprecated keys, future
///   fields).</description></item>
///   <item><description><see cref="TuplesSkipped"/> — tuples for which a
///   <c>local_agreement_profiles</c> row already existed before the migration ran
///   (idempotency guard fired).</description></item>
/// </list>
/// </summary>
public sealed record MigrationResult(
    int ProfilesCreated,
    int RowsMigrated,
    int RowsDroppedDuplicates,
    int RowsDroppedInformational,
    int RowsDroppedUnknown,
    int TuplesSkipped);

/// <summary>
/// Thrown by <see cref="LocalAgreementProfileMigrator.RebuildAsync"/> when a
/// post-migration validation assertion fails. The migration transaction is rolled
/// back before this exception is raised, so the pre-migration database state is
/// preserved for re-run after the operator addresses the cause.
/// </summary>
public sealed class MigrationValidationException : Exception
{
    public MigrationValidationException(string message) : base(message) { }
    public MigrationValidationException(string message, Exception innerException) : base(message, innerException) { }
}
