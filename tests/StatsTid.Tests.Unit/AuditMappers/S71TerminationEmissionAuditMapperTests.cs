using System.Text.Json;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S71 / ADR-033 slice 3b (TASK-7101, SPRINT-71 R5/R6/R10) — projection-shape tests for the three
/// new ADR-026 audit mappers (<see cref="TerminationPayoutRequestedAuditMapper"/>,
/// <see cref="TerminationClaimWaivedAuditMapper"/>, <see cref="SettlementReversedAuditMapper"/> —
/// the FIRST mapper for the previously define-only <c>SettlementReversed</c>). Each mapper is
/// pinned on:
/// <list type="bullet">
///   <item>TENANT_TARGETED scope with target_org_id = <c>context.ResolvedTargetOrgId</c>
///         (employee → users.primary_org_id, resolved at the dispatch site per ADR-026 D2)
///         and target_resource_id = employee_id;</item>
///   <item>the details-JSON field set;</item>
///   <item>NULL-TOLERANCE (S66 e0d1dc3 lesson): <c>Map</c> must not NRE on an
///         <c>Activator.CreateInstance</c>-built event whose <c>required</c> reference
///         members were bypassed — the catalog-driven visibility test constructs events
///         exactly that way.</item>
/// </list>
/// </summary>
public class S71TerminationEmissionAuditMapperTests
{
    private static readonly AuditProjectionContext Context = new(
        ActorId: "HR001",
        ActorPrimaryOrgId: "ORG_A",
        CorrelationId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow,
        ResolvedTargetOrgId: "ORG_EMP");

    // ── TerminationPayoutRequested (§26 anmodning, R6) ──────────────────────────────

    [Fact]
    public void TerminationPayoutRequested_Mapper_ProjectsTenantTargetedRow()
    {
        var mapper = new TerminationPayoutRequestedAuditMapper();
        var @event = new TerminationPayoutRequested
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            RequestDate = new DateOnly(2026, 9, 10),
            EvidenceNote = "Skriftlig anmodning modtaget.",
            CrystallizedDays = 11.25m,
            SettlementBoundaryDate = new DateOnly(2026, 7, 31),
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_EMP", row.TargetOrgId);
        Assert.Equal("EMP042", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("TerminationPayoutRequested", root.GetProperty("kind").GetString());
        Assert.Equal("§26", root.GetProperty("paragraph").GetString());
        Assert.Equal("EMP042", root.GetProperty("employeeId").GetString());
        Assert.Equal("VACATION", root.GetProperty("entitlementType").GetString());
        Assert.Equal(2025, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, root.GetProperty("settlementSequence").GetInt32());
        Assert.Equal("2026-09-10", root.GetProperty("requestDate").GetString());
        Assert.Equal("Skriftlig anmodning modtaget.", root.GetProperty("evidenceNote").GetString());
        Assert.Equal(11.25m, root.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal("2026-07-31", root.GetProperty("settlementBoundaryDate").GetString());
    }

    [Fact]
    public void TerminationPayoutRequested_Mapper_OmitsNullEvidenceNote()
    {
        var mapper = new TerminationPayoutRequestedAuditMapper();
        var @event = new TerminationPayoutRequested
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            RequestDate = new DateOnly(2026, 9, 10),
            EvidenceNote = null,
            CrystallizedDays = 11.25m,
            SettlementBoundaryDate = new DateOnly(2026, 7, 31),
        };

        var row = mapper.Map(@event, Context);

        using var details = JsonDocument.Parse(row.DetailsJson);
        // Null evidence is omitted under WhenWritingNull — never projected as JSON null.
        Assert.False(details.RootElement.TryGetProperty("evidenceNote", out _));
    }

    [Fact]
    public void TerminationPayoutRequested_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new TerminationPayoutRequestedAuditMapper();
        var @event = (TerminationPayoutRequested)Activator.CreateInstance(typeof(TerminationPayoutRequested))!;

        var row = mapper.Map(@event, Context); // must not NRE (required refs bypassed)

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal(string.Empty, row.TargetResourceId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.Equal("TerminationPayoutRequested", details.RootElement.GetProperty("kind").GetString());
    }

    // ── TerminationClaimWaived (§7 waive-in-full, R5/D-C) ───────────────────────────

    [Fact]
    public void TerminationClaimWaived_Mapper_ProjectsTenantTargetedRow()
    {
        var mapper = new TerminationClaimWaivedAuditMapper();
        var @event = new TerminationClaimWaived
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            WaivedDays = 3.5m,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_EMP", row.TargetOrgId);
        Assert.Equal("EMP042", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("TerminationClaimWaived", root.GetProperty("kind").GetString());
        Assert.Equal("§7", root.GetProperty("paragraph").GetString());
        // The R5 disposition discriminator: a waived claim must never read as §34 forfeiture.
        Assert.Equal("WAIVED", root.GetProperty("disposition").GetString());
        Assert.Equal("EMP042", root.GetProperty("employeeId").GetString());
        Assert.Equal("VACATION", root.GetProperty("entitlementType").GetString());
        Assert.Equal(2025, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, root.GetProperty("settlementSequence").GetInt32());
        Assert.Equal(3.5m, root.GetProperty("waivedDays").GetDecimal());
    }

    [Fact]
    public void TerminationClaimWaived_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new TerminationClaimWaivedAuditMapper();
        var @event = (TerminationClaimWaived)Activator.CreateInstance(typeof(TerminationClaimWaived))!;

        var row = mapper.Map(@event, Context); // must not NRE

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal(string.Empty, row.TargetResourceId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.Equal("TerminationClaimWaived", details.RootElement.GetProperty("kind").GetString());
    }

    // ── SettlementReversed (R10 — first mapper for the define-only event) ───────────

    [Fact]
    public void SettlementReversed_Mapper_ProjectsTenantTargetedRow_Superseded()
    {
        var mapper = new SettlementReversedAuditMapper();
        var @event = new SettlementReversed
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            ReversalKind = SettlementReversed.ReversalKindSuperseded,
            SuccessorSequence = 3,
            Trigger = "TERMINATION",
            Snapshot = new VacationSettlementSnapshot
            {
                Earned = 18.75m,
                Used = 10m,
                OkVersion = "OK24",
                CrystallizedDays = 11.25m,
            },
            TransferDays = 0m,
            PayoutDays = 0m,
            ForfeitDays = 0m,
            CrystallizedDays = 11.25m,
            ClaimDispositionDays = 3.5m,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_EMP", row.TargetOrgId);
        Assert.Equal("EMP042", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("SettlementReversed", root.GetProperty("kind").GetString());
        Assert.Equal("EMP042", root.GetProperty("employeeId").GetString());
        Assert.Equal("VACATION", root.GetProperty("entitlementType").GetString());
        Assert.Equal(2025, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, root.GetProperty("settlementSequence").GetInt32());
        Assert.Equal("SUPERSEDED", root.GetProperty("reversalKind").GetString());
        Assert.Equal(3, root.GetProperty("successorSequence").GetInt32());
        Assert.Equal("TERMINATION", root.GetProperty("trigger").GetString());
        // Per-bucket quantities are positive day-counts (direction = line_kind, R8).
        Assert.Equal(0m, root.GetProperty("transferDays").GetDecimal());
        Assert.Equal(0m, root.GetProperty("payoutDays").GetDecimal());
        Assert.Equal(0m, root.GetProperty("forfeitDays").GetDecimal());
        Assert.Equal(11.25m, root.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal(3.5m, root.GetProperty("claimDispositionDays").GetDecimal());
        Assert.Equal("OK24", root.GetProperty("okVersion").GetString());
    }

    [Fact]
    public void SettlementReversed_Mapper_Bare_OmitsNullOptionals()
    {
        var mapper = new SettlementReversedAuditMapper();
        var @event = new SettlementReversed
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            ReversalKind = SettlementReversed.ReversalKindBare,
            SuccessorSequence = null,
            Trigger = "YEAR_END",
            Snapshot = null,
            TransferDays = 0m,
            PayoutDays = 4.17m,
            ForfeitDays = 0m,
            CrystallizedDays = null,
            ClaimDispositionDays = null,
        };

        var row = mapper.Map(@event, Context);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("BARE", root.GetProperty("reversalKind").GetString());
        Assert.Equal("YEAR_END", root.GetProperty("trigger").GetString());
        Assert.Equal(4.17m, root.GetProperty("payoutDays").GetDecimal());
        // Null optionals (BARE/no-snapshot/non-TERMINATION) are omitted under WhenWritingNull.
        Assert.False(root.TryGetProperty("successorSequence", out _));
        Assert.False(root.TryGetProperty("crystallizedDays", out _));
        Assert.False(root.TryGetProperty("claimDispositionDays", out _));
        Assert.False(root.TryGetProperty("okVersion", out _));
    }

    [Fact]
    public void SettlementReversed_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new SettlementReversedAuditMapper();
        var @event = (SettlementReversed)Activator.CreateInstance(typeof(SettlementReversed))!;

        var row = mapper.Map(@event, Context); // must not NRE (Snapshot null + required refs bypassed)

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal(string.Empty, row.TargetResourceId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        var root = details.RootElement;
        Assert.Equal("SettlementReversed", root.GetProperty("kind").GetString());
        // Snapshot-derived operands are omitted (null) — never thrown on.
        Assert.False(root.TryGetProperty("okVersion", out _));
    }
}
