using Npgsql;

namespace StatsTid.Infrastructure;

public sealed class CompensatoryRestEntry
{
    public required Guid Id { get; init; }
    public required string EmployeeId { get; init; }
    public required DateOnly SourceDate { get; init; }
    public DateOnly? CompensatoryDate { get; init; }
    public required decimal Hours { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class CompensatoryRestRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public CompensatoryRestRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateAsync(string employeeId, DateOnly sourceDate, decimal hours, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO compensatory_rest (id, employee_id, source_date, hours) VALUES (@id, @employeeId, @sourceDate, @hours)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("sourceDate", sourceDate);
        cmd.Parameters.AddWithValue("hours", hours);
        await cmd.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<CompensatoryRestEntry>> GetByEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, employee_id, source_date, compensatory_date, hours, status, created_at FROM compensatory_rest WHERE employee_id = @employeeId ORDER BY source_date DESC", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);

        var entries = new List<CompensatoryRestEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new CompensatoryRestEntry
            {
                Id = reader.GetGuid(0),
                EmployeeId = reader.GetString(1),
                SourceDate = DateOnly.FromDateTime(reader.GetDateTime(2)),
                CompensatoryDate = reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
                Hours = reader.GetDecimal(4),
                Status = reader.GetString(5),
                CreatedAt = reader.GetDateTime(6),
            });
        }
        return entries;
    }

    public async Task<bool> GrantAsync(Guid id, DateOnly compensatoryDate, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE compensatory_rest SET status = 'GRANTED', compensatory_date = @compensatoryDate WHERE id = @id AND status = 'PENDING'", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("compensatoryDate", compensatoryDate);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
