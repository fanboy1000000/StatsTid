using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// The production <see cref="IOrganisationHistoryReader"/>: a read-only cross-context read of
/// <c>user_organisations</c>, a table the Backend owns and writes (<c>UserOrganisationRepository</c>, with its
/// audit rows). Payroll only reads it and never writes it, the mirror of ADR-034 D4, where the Backend reads
/// <c>payroll_export_records</c>, which Payroll owns. The statement is the
/// <c>UserAgreementCodeRepository.GetEffectiveFromDatesAsync</c> date read on the organisation table: no
/// <c>effective_to</c> filter (a closed row's start is still a change date), the bounds strict below and
/// inclusive above.
/// </summary>
public sealed class PostgresOrganisationHistoryReader : IOrganisationHistoryReader
{
    private const string EffectiveFromDatesSql =
        """
        SELECT DISTINCT effective_from
        FROM user_organisations
        WHERE user_id = @userId
          AND effective_from > @afterExclusive
          AND effective_from <= @toInclusive
        ORDER BY effective_from
        """;

    private readonly DbConnectionFactory _connectionFactory;

    public PostgresOrganisationHistoryReader(DbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DateOnly>> GetEffectiveFromDatesAsync(
        string employeeId, DateOnly afterExclusive, DateOnly toInclusive, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeId);

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(EffectiveFromDatesSql, conn);
        cmd.Parameters.AddWithValue("userId", employeeId);
        cmd.Parameters.AddWithValue("afterExclusive", afterExclusive);
        cmd.Parameters.AddWithValue("toInclusive", toInclusive);
        var dates = new List<DateOnly>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        return dates;
    }
}
