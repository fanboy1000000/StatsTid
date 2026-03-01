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

    public async Task<WageTypeMapping?> GetMappingAsync(
        string timeType, string okVersion, string agreementCode, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT time_type, wage_type, ok_version, agreement_code, description
            FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
            """, conn);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            _logger.LogWarning("No mapping found for {TimeType}/{OkVersion}/{Agreement}", timeType, okVersion, agreementCode);
            return null;
        }

        return new WageTypeMapping
        {
            TimeType = reader.GetString(0),
            WageType = reader.GetString(1),
            OkVersion = reader.GetString(2),
            AgreementCode = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    public async Task<IReadOnlyList<PayrollExportLine>> MapCalculationResultAsync(
        CalculationResult result, EmploymentProfile profile, CancellationToken ct = default)
    {
        var lines = new List<PayrollExportLine>();

        foreach (var lineItem in result.LineItems)
        {
            var mapping = await GetMappingAsync(lineItem.TimeType, profile.OkVersion, profile.AgreementCode, ct);
            if (mapping is null) continue;

            lines.Add(new PayrollExportLine
            {
                EmployeeId = profile.EmployeeId,
                WageType = mapping.WageType,
                Hours = lineItem.Hours,
                Amount = lineItem.Hours * lineItem.Rate,
                PeriodStart = lineItem.Date,
                PeriodEnd = lineItem.Date,
                OkVersion = profile.OkVersion
            });
        }

        return lines;
    }
}
