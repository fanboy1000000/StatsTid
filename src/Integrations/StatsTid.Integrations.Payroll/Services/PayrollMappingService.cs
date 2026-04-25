using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

public sealed class PayrollMappingService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ILogger<PayrollMappingService> _logger;

    public PayrollMappingService(DbConnectionFactory connectionFactory, ILogger<PayrollMappingService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Position-aware wage type mapping lookup.
    /// Canonical convention: empty string (<c>''</c>) in the <c>position</c> column is the
    /// generic fallback. The DB column is NOT NULL with DEFAULT '', so we must never query
    /// <c>position IS NULL</c> (Codex BLOCKER fix, TASK-1802).
    /// Precedence: position-specific match wins over the generic ('') row.
    /// </summary>
    public async Task<WageTypeMapping?> GetMappingAsync(
        string timeType, string okVersion, string agreementCode, string? position = null, CancellationToken ct = default)
    {
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
    /// Maps a CalculationResult to PayrollExportLines using wage type mappings.
    /// Accepts optional position for position-aware mapping lookup; null/empty == generic.
    /// </summary>
    public async Task<IReadOnlyList<PayrollExportLine>> MapCalculationResultAsync(
        CalculationResult result, EmploymentProfile profile, string? position = null, CancellationToken ct = default)
    {
        var lines = new List<PayrollExportLine>();

        foreach (var lineItem in result.LineItems)
        {
            var mapping = await GetMappingAsync(lineItem.TimeType, profile.OkVersion, profile.AgreementCode, position, ct);
            if (mapping is null) continue;

            lines.Add(new PayrollExportLine
            {
                EmployeeId = profile.EmployeeId,
                WageType = mapping.WageType,
                Hours = lineItem.Hours,
                Amount = lineItem.Hours * lineItem.Rate,
                PeriodStart = lineItem.Date,
                PeriodEnd = lineItem.Date,
                OkVersion = profile.OkVersion,
                SourceRuleId = result.RuleId,
                SourceTimeType = lineItem.TimeType
            });
        }

        return lines;
    }
}
