using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S68 / TASK-6804 (ADR-033 D8). DB-facing surface for <c>vacation_transfer_agreements</c> —
/// the §21 stk.2 <b>written</b> transfer-agreement record (one per
/// <c>(employee_id, entitlement_year, entitlement_type)</c>). The agreement records how many
/// of the &gt;4-week tranche the employee+employer agreed to carry into next ferieår instead
/// of taking the law's §24 auto-payout default. It is a pure record here: the §21 stk.2 31-Dec
/// deadline + the VACATION-only + transfer-cap + reject-post-settlement legal guards are
/// enforced in the <b>endpoint</b> (TASK-6806), NOT this repository (ADR-033 D8; mirrors the
/// init.sql comment "31-Dec deadline enforced in endpoint not DB").
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D3/D5).</b> The write path takes the caller-supplied
/// <c>(conn, tx)</c> and appends the <c>vacation_transfer_agreement_audit</c> row in the same
/// transaction (ADR-019 D8 version-transition columns — mirrors
/// <see cref="EntitlementConfigRepository.AppendAuditAsync"/>). The caller commits or rolls
/// back; this method does NOT.
/// </para>
///
/// <para>
/// The settlement pass (<see cref="VacationSettlementService"/>) consumes
/// <see cref="GetByKeyAsync(NpgsqlConnection, NpgsqlTransaction, string, int, string, CancellationToken)"/>
/// inside its transaction to read the §21 <c>transfer_days</c> into the immutable snapshot
/// (ADR-033 D3) — the §21 provenance component of the next-year carryover (D6).
/// </para>
/// </summary>
public sealed class VacationTransferAgreementRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public VacationTransferAgreementRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Self-managed-connection read of the §21 agreement for a natural key, or <c>null</c>
    /// when none exists (the law's default is §24 auto-payout). Used by the endpoint's
    /// reject-post-settlement / If-Match precondition reads.
    /// </summary>
    public async Task<VacationTransferAgreement?> GetByKeyAsync(
        string employeeId, int entitlementYear, string entitlementType, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetByKeyAsync(conn, null, employeeId, entitlementYear, entitlementType, ct);
    }

    /// <summary>
    /// In-transaction sibling overload (ADR-018 D3). Reuses the caller-supplied
    /// <paramref name="conn"/> + <paramref name="tx"/> so the read observes the same snapshot
    /// as the settlement transaction. The settlement pass reads the §21 <c>transfer_days</c>
    /// through this overload while holding the advisory lock.
    /// </summary>
    public async Task<VacationTransferAgreement?> GetByKeyAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, int entitlementYear, string entitlementType, CancellationToken ct = default)
        => await ExecuteGetByKeyAsync(conn, tx, employeeId, entitlementYear, entitlementType, ct);

    private static async Task<VacationTransferAgreement?> ExecuteGetByKeyAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, int entitlementYear, string entitlementType, CancellationToken ct)
    {
        const string sql =
            """
            SELECT employee_id, entitlement_year, entitlement_type, transfer_days,
                   agreement_date, recorded_by, version, created_at, updated_at
            FROM vacation_transfer_agreements
            WHERE employee_id = @employeeId
              AND entitlement_year = @entitlementYear
              AND entitlement_type = @entitlementType
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAgreement(reader) : null;
    }

    /// <summary>
    /// In-transaction insert of a fresh §21 agreement at version 1 (ADR-018 D5). Appends the
    /// CREATED audit row in the same tx. The endpoint owns the legal guards + the
    /// reject-post-settlement precondition before calling this. Returns the persisted record.
    /// </summary>
    public async Task<VacationTransferAgreement> InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationTransferAgreement agreement, string actorId, string actorRole,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_transfer_agreements (
                employee_id, entitlement_year, entitlement_type,
                transfer_days, agreement_date, recorded_by, version)
            VALUES (
                @employeeId, @entitlementYear, @entitlementType,
                @transferDays, @agreementDate, @recordedBy, 1)
            RETURNING employee_id, entitlement_year, entitlement_type, transfer_days,
                      agreement_date, recorded_by, version, created_at, updated_at
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", agreement.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementYear", agreement.EntitlementYear);
        cmd.Parameters.AddWithValue("entitlementType", agreement.EntitlementType);
        cmd.Parameters.AddWithValue("transferDays", agreement.TransferDays);
        cmd.Parameters.AddWithValue("agreementDate", agreement.AgreementDate);
        cmd.Parameters.AddWithValue("recordedBy", agreement.RecordedBy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"VacationTransferAgreementRepository.InsertAsync produced no row for " +
                $"(employee_id='{agreement.EmployeeId}', entitlement_year={agreement.EntitlementYear}, " +
                $"entitlement_type='{agreement.EntitlementType}').");
        }
        var inserted = ReadAgreement(reader);
        await reader.DisposeAsync();

        await AppendAuditAsync(
            conn, tx, inserted, "CREATED",
            previousData: null, newData: SerializeAgreement(inserted),
            versionBefore: null, versionAfter: inserted.Version,
            actorId, actorRole, ct);
        return inserted;
    }

    /// <summary>
    /// In-transaction same-key UPDATE with optimistic-concurrency (If-Match) check + version bump
    /// (ADR-019). The endpoint validated the legal guards + the reject-post-settlement precondition
    /// before calling. Throws <see cref="OptimisticConcurrencyException"/> when
    /// <paramref name="expectedVersion"/> != the stored version (endpoint maps to 412). Appends the
    /// UPDATED audit row in the same tx. Returns the persisted record.
    /// </summary>
    public async Task<VacationTransferAgreement> UpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationTransferAgreement agreement, long expectedVersion, string actorId, string actorRole,
        CancellationToken ct = default)
    {
        var previous = await ExecuteGetByKeyAsync(
            conn, tx, agreement.EmployeeId, agreement.EntitlementYear, agreement.EntitlementType, ct)
            ?? throw new OptimisticConcurrencyException(
                $"No §21 transfer agreement exists for (employee_id='{agreement.EmployeeId}', " +
                $"entitlement_year={agreement.EntitlementYear}, entitlement_type='{agreement.EntitlementType}') to update.",
                expectedVersion: expectedVersion, actualVersion: null);

        if (previous.Version != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"§21 transfer agreement version is {previous.Version}, but caller sent If-Match: " +
                $"\"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion, actualVersion: previous.Version);
        }

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE vacation_transfer_agreements SET
                transfer_days = @transferDays,
                agreement_date = @agreementDate,
                recorded_by = @recordedBy,
                version = version + 1,
                updated_at = NOW()
            WHERE employee_id = @employeeId
              AND entitlement_year = @entitlementYear
              AND entitlement_type = @entitlementType
              AND version = @expectedVersion
            RETURNING employee_id, entitlement_year, entitlement_type, transfer_days,
                      agreement_date, recorded_by, version, created_at, updated_at
            """, conn, tx);
        cmd.Parameters.AddWithValue("transferDays", agreement.TransferDays);
        cmd.Parameters.AddWithValue("agreementDate", agreement.AgreementDate);
        cmd.Parameters.AddWithValue("recordedBy", agreement.RecordedBy);
        cmd.Parameters.AddWithValue("employeeId", agreement.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementYear", agreement.EntitlementYear);
        cmd.Parameters.AddWithValue("entitlementType", agreement.EntitlementType);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth: the version check above already validated the token; a 0-row
            // UPDATE here means a concurrent writer slipped in between the read and the UPDATE.
            throw new OptimisticConcurrencyException(
                $"§21 transfer agreement concurrent-update conflict for (employee_id='{agreement.EmployeeId}', " +
                $"entitlement_year={agreement.EntitlementYear}, entitlement_type='{agreement.EntitlementType}').",
                expectedVersion: expectedVersion, actualVersion: null);
        }
        var updated = ReadAgreement(reader);
        await reader.DisposeAsync();

        await AppendAuditAsync(
            conn, tx, updated, "UPDATED",
            previousData: SerializeAgreement(previous), newData: SerializeAgreement(updated),
            versionBefore: previous.Version, versionAfter: updated.Version,
            actorId, actorRole, ct);
        return updated;
    }

    /// <summary>
    /// In-transaction audit insert (ADR-018 D5 + ADR-019 D8 version-transition columns). Mirrors
    /// <see cref="EntitlementConfigRepository.AppendAuditAsync"/>; writes the
    /// <c>vacation_transfer_agreement_audit</c> row (action CHECK ∈ CREATED/UPDATED/DELETED/SUPERSEDED).
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationTransferAgreement agreement, string action,
        string? previousData, string? newData,
        long? versionBefore, long? versionAfter,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_transfer_agreement_audit
                (employee_id, entitlement_year, entitlement_type, action,
                 previous_data, new_data, version_before, version_after,
                 actor_id, actor_role)
            VALUES
                (@employeeId, @entitlementYear, @entitlementType, @action,
                 @previousData::jsonb, @newData::jsonb, @versionBefore, @versionAfter,
                 @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", agreement.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementYear", agreement.EntitlementYear);
        cmd.Parameters.AddWithValue("entitlementType", agreement.EntitlementType);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", (object?)versionAfter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string SerializeAgreement(VacationTransferAgreement a) =>
        JsonSerializer.Serialize(new
        {
            a.EmployeeId,
            a.EntitlementYear,
            a.EntitlementType,
            a.TransferDays,
            AgreementDate = a.AgreementDate.ToString("yyyy-MM-dd"),
            a.RecordedBy,
            a.Version,
        }, AuditJson);

    private static VacationTransferAgreement ReadAgreement(NpgsqlDataReader reader) => new()
    {
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        EntitlementYear = reader.GetInt32(reader.GetOrdinal("entitlement_year")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        TransferDays = reader.GetDecimal(reader.GetOrdinal("transfer_days")),
        AgreementDate = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("agreement_date")),
        RecordedBy = reader.GetString(reader.GetOrdinal("recorded_by")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
    };
}

/// <summary>
/// S68 / TASK-6804 — repo-row DTO for <c>vacation_transfer_agreements</c> (§21 stk.2 written
/// transfer agreement, ADR-033 D8). Co-located with its repository (the
/// <see cref="SaveEntitlementConfigResult"/> precedent) — an Infrastructure row record, not a
/// cross-process SharedKernel contract. Init-only (PAT-001). <see cref="NpgsqlDbType"/> mapping
/// is handled in the repository; <see cref="AgreementDate"/> is a calendar <see cref="DateOnly"/>
/// (the §21 stk.2 deadline operand, compared against the Copenhagen business clock in the endpoint).
/// </summary>
public sealed record VacationTransferAgreement
{
    public required string EmployeeId { get; init; }
    public required int EntitlementYear { get; init; }
    public required string EntitlementType { get; init; }

    /// <summary>The agreed §21 transfer days (the &gt;4-week tranche carried to next ferieår).</summary>
    public required decimal TransferDays { get; init; }

    /// <summary>The date the written agreement was made (§21 stk.2 evidence; ≤ 31 Dec deadline, endpoint-checked).</summary>
    public required DateOnly AgreementDate { get; init; }

    /// <summary>The HR actor (users.user_id) who recorded the agreement.</summary>
    public required string RecordedBy { get; init; }

    /// <summary>ADR-019 If-Match row version (1 on insert, bumped per in-place edit).</summary>
    public long Version { get; init; } = 1;

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
