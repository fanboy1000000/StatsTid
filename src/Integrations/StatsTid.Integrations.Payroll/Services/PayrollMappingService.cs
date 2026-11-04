using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

public sealed class PayrollMappingService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly WageTypeMappingRepository? _wageTypeMappingRepository;
    private readonly ILogger<PayrollMappingService> _logger;

    public PayrollMappingService(
        DbConnectionFactory connectionFactory,
        ILogger<PayrollMappingService> logger,
        WageTypeMappingRepository? wageTypeMappingRepository = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _wageTypeMappingRepository = wageTypeMappingRepository;
    }

    /// <summary>
    /// Position-aware wage type mapping lookup.
    /// Canonical convention: empty string (<c>''</c>) in the <c>position</c> column is the
    /// generic fallback. The DB column is NOT NULL with DEFAULT '', so we must never query
    /// <c>position IS NULL</c> (Codex BLOCKER fix, TASK-1802).
    /// Precedence: position-specific match wins over the generic ('') row.
    ///
    /// <para>
    /// ADR-020 D1 / Implications §7 (S29 TASK-2907): when <paramref name="asOfDate"/> is
    /// supplied, the lookup routes through
    /// <see cref="WageTypeMappingRepository.GetByKeyAtAsync"/> for an effective-dated read
    /// (closed rows queryable at past dates per S29 effective-dating). When null, the
    /// existing current-row query path is preserved unchanged (Implications §7 "no retroactive
    /// recomputation"). The planner-driven per-segment export
    /// (<c>PCS.MapSegmentToExportLinesAsync</c>) passes <c>asOfDate = segmentStart</c>, and since S147
    /// TASK-14716 it is the only production caller: the current-row path's production caller, a per-line
    /// mapper that served only the raw export routes, was deleted with them.
    /// </para>
    /// </summary>
    public async Task<WageTypeMapping?> GetMappingAsync(
        string timeType, string okVersion, string agreementCode, string? position = null,
        CancellationToken ct = default, DateOnly? asOfDate = null)
    {
        // ADR-020 D1 / Implications §7 — dated read for the planner-driven export path
        // (PCS.MapSegmentToExportLinesAsync passes asOfDate = segmentStart). The repo
        // method replicates the (position = @position OR position = '') fallback at the
        // dated read (TASK-2904 Assumption #17 in REFINEMENT-s29-phase-4d1.md); we do
        // NOT duplicate the fallback logic here.
        if (asOfDate.HasValue)
        {
            if (_wageTypeMappingRepository is null)
            {
                throw new InvalidOperationException(
                    "PayrollMappingService.GetMappingAsync was called with asOfDate set but " +
                    "no WageTypeMappingRepository was supplied at construction. The dated-read " +
                    "path (ADR-020 D1) requires the repository dependency. Wire it in DI.");
            }
            return await _wageTypeMappingRepository.GetByKeyAtAsync(
                timeType, okVersion, agreementCode, position ?? "", asOfDate.Value, ct);
        }

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        NpgsqlCommand cmd;

        // Treat null or empty position identically as "generic lookup".
        if (!string.IsNullOrEmpty(position))
        {
            // Position provided: prefer position-specific row, fall back to generic ('').
            // ORDER BY (position = '') ASC puts the specific match first (false < true).
            cmd = new NpgsqlCommand(
                """
                SELECT time_type, wage_type, ok_version, agreement_code, position, description
                FROM wage_type_mappings
                WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
                  AND (position = @position OR position = '')
                ORDER BY (position = '') ASC
                LIMIT 1
                """, conn);
            cmd.Parameters.AddWithValue("timeType", timeType);
            cmd.Parameters.AddWithValue("okVersion", okVersion);
            cmd.Parameters.AddWithValue("agreementCode", agreementCode);
            cmd.Parameters.AddWithValue("position", position);
        }
        else
        {
            // Generic-only lookup: match the canonical empty-string row.
            cmd = new NpgsqlCommand(
                """
                SELECT time_type, wage_type, ok_version, agreement_code, position, description
                FROM wage_type_mappings
                WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
                  AND position = ''
                LIMIT 1
                """, conn);
            cmd.Parameters.AddWithValue("timeType", timeType);
            cmd.Parameters.AddWithValue("okVersion", okVersion);
            cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        }

        await using (cmd)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                _logger.LogWarning("No mapping found for {TimeType}/{OkVersion}/{Agreement}/{Position}",
                    timeType, okVersion, agreementCode, string.IsNullOrEmpty(position) ? "(generic)" : position);
                return null;
            }

            // position column is NOT NULL (DEFAULT ''), so GetString is safe.
            return new WageTypeMapping
            {
                TimeType = reader.GetString(0),
                WageType = reader.GetString(1),
                OkVersion = reader.GetString(2),
                AgreementCode = reader.GetString(3),
                Position = reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5)
            };
        }
    }

    /// <summary>
    /// Canonical <see cref="PayrollExportLine"/> shape constructor — the one source of truth for an export
    /// line's shape, used by <c>PeriodCalculationService.MapSegmentToExportLinesAsync</c> (the planner-driven,
    /// per-segment path) with the segment's dates and OK version. (Its per-line-date caller served only the
    /// raw export routes and was deleted with them in S147 TASK-14716.)
    ///
    /// <para>
    /// Internal so PCS can call it without re-exposing the type via a public surface;
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> on
    /// <c>StatsTid.Integrations.Payroll</c> is not needed because the consumer lives in the
    /// same assembly.
    /// </para>
    /// </summary>
    internal static PayrollExportLine BuildLine(
        CalculationLineItem lineItem,
        CalculationResult result,
        EmploymentProfile profile,
        WageTypeMapping mapping,
        DateOnly periodStart,
        DateOnly periodEnd,
        string okVersion,
        Guid manifestId)
        => new()
        {
            EmployeeId = profile.EmployeeId,
            WageType = mapping.WageType,
            Hours = lineItem.Hours,
            Amount = lineItem.Hours * lineItem.Rate,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OkVersion = okVersion,
            SourceRuleId = result.RuleId,
            SourceTimeType = lineItem.TimeType,
            ManifestId = manifestId,
        };
}
