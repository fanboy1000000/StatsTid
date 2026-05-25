using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Events;
using StatsTid.Infrastructure.Outbox;

namespace StatsTid.Infrastructure;

public sealed class DelegationExpiryService : BackgroundService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly ILogger<DelegationExpiryService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    public DelegationExpiryService(
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        ILogger<DelegationExpiryService> logger)
    {
        _connectionFactory = connectionFactory;
        _outbox = outbox;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseExpiredDelegationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DelegationExpiryService: error closing expired delegations");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CloseExpiredDelegationsAsync(CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Find expired self-delegation ACTING lines.
        var expiredLines = new List<(Guid LineId, string EmployeeId, string ManagerId, string TreeRootOrgId, long Version, DateOnly ScheduledExpiry)>();

        await using (var findCmd = new NpgsqlCommand(
            """
            SELECT reporting_line_id, employee_id, manager_id, tree_root_org_id, version, scheduled_expiry
            FROM reporting_lines
            WHERE source = 'SELF_DELEGATION'
              AND relationship = 'ACTING'
              AND scheduled_expiry IS NOT NULL
              AND scheduled_expiry <= CURRENT_DATE
              AND effective_to IS NULL
            """, conn))
        {
            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                expiredLines.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    DateOnly.FromDateTime(reader.GetDateTime(5))
                ));
            }
        }

        if (expiredLines.Count == 0) return;

        _logger.LogInformation("DelegationExpiryService: closing {Count} expired delegation lines", expiredLines.Count);

        // Close each line atomically: tx -> UPDATE -> outbox event -> commit (ADR-018 D3).
        foreach (var (lineId, employeeId, managerId, treeRootOrgId, version, scheduledExpiry) in expiredLines)
        {
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            try
            {
                await using var closeCmd = new NpgsqlCommand(
                    """
                    UPDATE reporting_lines
                    SET effective_to = scheduled_expiry, version = version + 1
                    WHERE reporting_line_id = @lineId
                      AND effective_to IS NULL
                    RETURNING version
                    """, conn, tx);
                closeCmd.Parameters.AddWithValue("lineId", lineId);
                var newVersion = await closeCmd.ExecuteScalarAsync(ct);
                if (newVersion is null)
                {
                    await tx.RollbackAsync(ct);
                    continue; // Already closed by another process.
                }

                // Emit ReportingLineSuperseded event.
                var @event = new ReportingLineSuperseded
                {
                    ReportingLineId = lineId,
                    EmployeeId = employeeId,
                    PreviousManagerId = managerId,
                    NewManagerId = null,
                    TreeRootOrgId = treeRootOrgId,
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), // approximation
                    EffectiveTo = scheduledExpiry,
                    RowVersion = (long)newVersion,
                    ActorId = "SYSTEM",
                    ActorRole = "SYSTEM",
                };
                await _outbox.EnqueueAsync(conn, tx, $"reporting-line-{employeeId}", @event, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation("DelegationExpiryService: closed expired delegation for employee {EmployeeId}", employeeId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(ex, "DelegationExpiryService: failed to close delegation line {LineId}", lineId);
            }
        }
    }
}
