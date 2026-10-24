using System.Text.Json;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Serialization;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.AuditMappers;

public sealed class RetroactiveCorrectionRequestedAuditMapper : IAuditProjectionMapper<RetroactiveCorrectionRequested>
{
    public AuditProjectionRowData Map(RetroactiveCorrectionRequested @event, AuditProjectionContext context)
    {
        var details = new
        {
            employeeId = @event.EmployeeId,
            originalPeriodStart = @event.OriginalPeriodStart,
            originalPeriodEnd = @event.OriginalPeriodEnd,
            agreementCode = @event.AgreementCode,
            okVersion = @event.OkVersion,
            reason = @event.Reason,
            correctedByActorId = @event.CorrectedByActorId,
            correctionLineCount = @event.CorrectionLineCount,
            totalDifferenceHours = @event.TotalDifferenceHours,
            manifestId = @event.ManifestId,
            // S145 TASK-14512 — Q7 (b), R37: the next month's pay period when this correction changed a
            // week that month already paid (the admin must recalculate it too). Null otherwise, and then
            // omitted (AuditMapperJsonOptions writes no nulls), so the details of every correction that
            // names nothing are byte-identical to before.
            nextPeriodToRecalculateStart = @event.NextPeriodToRecalculateStart,
            nextPeriodToRecalculateEnd = @event.NextPeriodToRecalculateEnd,
            // S146 TASK-14605 (C1, AC-C7; ADR-013 item 2): the opening the recalculation used, the month's closing
            // before and after, and the opening's provenance (UNTRACKED when flex is not tracked). Null on every
            // pre-S146 event, and then omitted, like the next-period keys above.
            openingFlexBalanceUsed = @event.OpeningFlexBalanceUsed,
            closingFlexBalanceBefore = @event.ClosingFlexBalanceBefore,
            closingFlexBalanceAfter = @event.ClosingFlexBalanceAfter,
            flexProvenance = @event.FlexProvenance,
        };
        return new AuditProjectionRowData(
            VisibilityScope: AuditVisibilityScope.TenantTargeted,
            TargetOrgId: context.ResolvedTargetOrgId,
            TargetResourceId: @event.EmployeeId,
            DetailsJson: JsonSerializer.Serialize(details, AuditMapperJsonOptions.Default));
    }
}
