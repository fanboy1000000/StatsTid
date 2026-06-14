using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.Infrastructure.Outbox;

namespace StatsTid.Infrastructure;

/// <summary>
/// Background poller that closes EXPIRED approver-owned vikar rows in
/// <c>manager_vikar</c> (S74 storage cutover — TASK-7401 R4). Each expired row is
/// closed atomically (tx → close → outbox <see cref="ManagerVikarEnded"/> → commit;
/// ADR-018 D3), one tx per row.
///
/// <para>
/// R4a inclusive "til og med" fix: <c>until_date</c> is the LAST covered day, so a row
/// expires (closes) the day AFTER — the poll selects <c>until_date &lt; CURRENT_DATE</c>
/// (STRICTLY less-than), NOT <c>&lt;=</c>. A vikar whose <c>until_date</c> is today is
/// STILL active today and is NOT closed until tomorrow.
/// </para>
/// </summary>
public sealed class DelegationExpiryService : BackgroundService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly ManagerVikarRepository _vikarRepo;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly IAuditProjectionMapper<ManagerVikarEnded> _endedAuditMapper;
    private readonly ILogger<DelegationExpiryService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    public DelegationExpiryService(
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        ManagerVikarRepository vikarRepo,
        AuditProjectionRepository auditRepo,
        IAuditProjectionMapper<ManagerVikarEnded> endedAuditMapper,
        ILogger<DelegationExpiryService> logger)
    {
        _connectionFactory = connectionFactory;
        _outbox = outbox;
        _vikarRepo = vikarRepo;
        _auditRepo = auditRepo;
        _endedAuditMapper = endedAuditMapper;
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

    /// <summary>
    /// Runs ONE expiry sweep (the body of the poll loop). Exposed for deterministic
    /// single-shot integration testing of the R4a inclusive-date boundary — production
    /// invokes it from <see cref="ExecuteAsync"/> on the 5-minute cadence.
    /// </summary>
    public async Task CloseExpiredDelegationsAsync(CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Find expired vikar rows. R4a: until_date < CURRENT_DATE (strictly), so the named
        // until_date is the LAST covered day and the row closes the day AFTER.
        var expired = new List<ManagerVikar>();
        await using (var findCmd = new NpgsqlCommand(
            """
            SELECT vikar_id, absent_approver_id, vikar_user_id, until_date, reason,
                   tree_root_org_id, version, created_by, created_at, effective_to
            FROM manager_vikar
            WHERE effective_to IS NULL
              AND until_date < CURRENT_DATE
            """, conn))
        {
            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                expired.Add(new ManagerVikar
                {
                    VikarId = reader.GetGuid(0),
                    AbsentApproverId = reader.GetString(1),
                    VikarUserId = reader.GetString(2),
                    UntilDate = DateOnly.FromDateTime(reader.GetDateTime(3)),
                    Reason = reader.GetString(4),
                    TreeRootOrgId = reader.GetString(5),
                    Version = reader.GetInt64(6),
                    CreatedBy = reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8),
                    EffectiveTo = reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                });
            }
        }

        if (expired.Count == 0) return;

        _logger.LogInformation("DelegationExpiryService: closing {Count} expired vikar rows", expired.Count);

        // Close each row atomically: tx → close → outbox event → commit (ADR-018 D3).
        foreach (var vikar in expired)
        {
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            try
            {
                // Close the day AFTER until_date (the first uncovered day). The close marker
                // records WHEN the row stopped being active, not the last covered day.
                var effectiveTo = vikar.UntilDate.AddDays(1);
                var closed = await _vikarRepo.CloseAsync(conn, tx, vikar.VikarId, effectiveTo, ct);
                if (closed is null)
                {
                    await tx.RollbackAsync(ct);
                    continue; // Already closed by another process.
                }

                var @event = new ManagerVikarEnded
                {
                    VikarId = closed.VikarId,
                    AbsentApproverId = closed.AbsentApproverId,
                    VikarUserId = closed.VikarUserId,
                    UntilDate = closed.UntilDate,
                    Reason = closed.Reason,
                    TreeRootOrgId = closed.TreeRootOrgId,
                    EffectiveTo = closed.EffectiveTo!.Value,
                    EndReason = "EXPIRED",
                    RowVersion = closed.Version,
                    ActorId = "SYSTEM",
                    ActorRole = "SYSTEM",
                };
                // ADR-018 D3 + ADR-026 D2: event + audit-projection row + state in ONE tx
                // (mirrors the SettlementCloseService system-actor flip site). The SYSTEM actor
                // carries no JWT org / correlation; actor_primary_org_id = the vikar's tree root.
                var outboxId = await _outbox.EnqueueAndReturnIdAsync(
                    conn, tx, $"reporting-line-{closed.AbsentApproverId}", @event, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: @event.ActorId,
                    ActorPrimaryOrgId: @event.TreeRootOrgId,
                    CorrelationId: @event.CorrelationId,
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)),
                    ResolvedTargetOrgId: @event.TreeRootOrgId);
                var auditRow = _endedAuditMapper.Map(@event, auditCtx);
                await _auditRepo.InsertAsync(
                    conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "DelegationExpiryService: closed expired vikar {VikarId} for approver {ApproverId}",
                    closed.VikarId, closed.AbsentApproverId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(ex, "DelegationExpiryService: failed to close vikar row {VikarId}", vikar.VikarId);
            }
        }
    }
}
