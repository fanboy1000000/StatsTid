using System.Reflection;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Unit.ReportingLine;

/// <summary>
/// Unit tests for the reporting-line feature (TASK-4812):
/// EventSerializer round-trip for all 4 event types, DEP-003 registration parity,
/// and ReportingLine model property verification.
/// </summary>
public class ReportingLineTests
{
    // ---------------------------------------------------------------
    // 1. EventSerializer round-trip: ReportingLineAssigned
    // ---------------------------------------------------------------
    [Fact]
    public void EventSerializer_RoundTrips_ReportingLineAssigned()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var reportingLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);

        var original = new ReportingLineAssigned
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "admin01",
            ActorRole = "GLOBAL_ADMIN",
            CorrelationId = correlationId,
            ReportingLineId = reportingLineId,
            EmployeeId = "emp001",
            ManagerId = "mgr01",
            TreeRootOrgId = "STY02",
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2024, 1, 1),
            Source = "MANUAL",
            RowVersion = 1,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("ReportingLineAssigned", json);

        var result = Assert.IsType<ReportingLineAssigned>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("ReportingLineAssigned", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("admin01", result.ActorId);
        Assert.Equal("GLOBAL_ADMIN", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(reportingLineId, result.ReportingLineId);
        Assert.Equal("emp001", result.EmployeeId);
        Assert.Equal("mgr01", result.ManagerId);
        Assert.Equal("STY02", result.TreeRootOrgId);
        Assert.Equal("PRIMARY", result.Relationship);
        Assert.Equal(new DateOnly(2024, 1, 1), result.EffectiveFrom);
        Assert.Equal("MANUAL", result.Source);
        Assert.Equal(1, result.RowVersion);
    }

    // ---------------------------------------------------------------
    // 2. EventSerializer round-trip: ReportingLineSuperseded
    // ---------------------------------------------------------------
    [Fact]
    public void EventSerializer_RoundTrips_ReportingLineSuperseded()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var reportingLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc);

        var original = new ReportingLineSuperseded
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "admin02",
            ActorRole = "ORG_ADMIN",
            CorrelationId = correlationId,
            ReportingLineId = reportingLineId,
            EmployeeId = "emp002",
            PreviousManagerId = "mgr01",
            NewManagerId = "mgr02",
            TreeRootOrgId = "STY03",
            EffectiveFrom = new DateOnly(2024, 1, 1),
            EffectiveTo = new DateOnly(2024, 6, 30),
            RowVersion = 2,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("ReportingLineSuperseded", json);

        var result = Assert.IsType<ReportingLineSuperseded>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("ReportingLineSuperseded", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("admin02", result.ActorId);
        Assert.Equal("ORG_ADMIN", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(reportingLineId, result.ReportingLineId);
        Assert.Equal("emp002", result.EmployeeId);
        Assert.Equal("mgr01", result.PreviousManagerId);
        Assert.Equal("mgr02", result.NewManagerId);
        Assert.Equal("STY03", result.TreeRootOrgId);
        Assert.Equal(new DateOnly(2024, 1, 1), result.EffectiveFrom);
        Assert.Equal(new DateOnly(2024, 6, 30), result.EffectiveTo);
        Assert.Equal(2, result.RowVersion);
    }

    [Fact]
    public void EventSerializer_RoundTrips_ReportingLineSuperseded_NullNewManagerId()
    {
        var original = new ReportingLineSuperseded
        {
            ReportingLineId = Guid.NewGuid(),
            EmployeeId = "emp003",
            PreviousManagerId = "mgr01",
            NewManagerId = null,
            TreeRootOrgId = "STY02",
            EffectiveFrom = new DateOnly(2024, 3, 1),
            EffectiveTo = new DateOnly(2024, 9, 30),
            RowVersion = 3,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("ReportingLineSuperseded", json);

        var result = Assert.IsType<ReportingLineSuperseded>(deserialized);
        Assert.Null(result.NewManagerId);
        Assert.Equal("emp003", result.EmployeeId);
        Assert.Equal("mgr01", result.PreviousManagerId);
    }

    // ---------------------------------------------------------------
    // 3. EventSerializer round-trip: ReportingLineBulkImported
    // ---------------------------------------------------------------
    [Fact]
    public void EventSerializer_RoundTrips_ReportingLineBulkImported()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

        var original = new ReportingLineBulkImported
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "system",
            ActorRole = "GLOBAL_ADMIN",
            CorrelationId = correlationId,
            BatchId = batchId,
            TreeRootOrgId = "STY02",
            LineCount = 47,
            Source = "CSV_IMPORT",
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("ReportingLineBulkImported", json);

        var result = Assert.IsType<ReportingLineBulkImported>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("ReportingLineBulkImported", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("system", result.ActorId);
        Assert.Equal("GLOBAL_ADMIN", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(batchId, result.BatchId);
        Assert.Equal("STY02", result.TreeRootOrgId);
        Assert.Equal(47, result.LineCount);
        Assert.Equal("CSV_IMPORT", result.Source);
    }

    // ---------------------------------------------------------------
    // 4. EventSerializer round-trip: ReportingLineManagerDeactivated
    // ---------------------------------------------------------------
    [Fact]
    public void EventSerializer_RoundTrips_ReportingLineManagerDeactivated()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var reportingLineId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 20, 13, 0, 0, DateTimeKind.Utc);

        var original = new ReportingLineManagerDeactivated
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "admin03",
            ActorRole = "GLOBAL_ADMIN",
            CorrelationId = correlationId,
            ReportingLineId = reportingLineId,
            EmployeeId = "emp005",
            ManagerId = "mgr03",
            TreeRootOrgId = "STY04",
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("ReportingLineManagerDeactivated", json);

        var result = Assert.IsType<ReportingLineManagerDeactivated>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("ReportingLineManagerDeactivated", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("admin03", result.ActorId);
        Assert.Equal("GLOBAL_ADMIN", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(reportingLineId, result.ReportingLineId);
        Assert.Equal("emp005", result.EmployeeId);
        Assert.Equal("mgr03", result.ManagerId);
        Assert.Equal("STY04", result.TreeRootOrgId);
    }

    // ---------------------------------------------------------------
    // 5. DEP-003: All 4 reporting-line event types registered
    // ---------------------------------------------------------------
    [Fact]
    public void EventSerializer_Registers_AllReportingLineEventTypes()
    {
        var mapField = typeof(EventSerializer).GetField(
            "EventTypeMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(mapField);
        var map = (IReadOnlyDictionary<string, Type>)mapField!.GetValue(null)!;

        var expectedRegistrations = new Dictionary<string, Type>
        {
            ["ReportingLineAssigned"] = typeof(ReportingLineAssigned),
            ["ReportingLineSuperseded"] = typeof(ReportingLineSuperseded),
            ["ReportingLineBulkImported"] = typeof(ReportingLineBulkImported),
            ["ReportingLineManagerDeactivated"] = typeof(ReportingLineManagerDeactivated),
        };

        foreach (var (discriminator, expectedType) in expectedRegistrations)
        {
            Assert.True(
                map.TryGetValue(discriminator, out var registeredType),
                $"EventSerializer.EventTypeMap is missing discriminator '{discriminator}'. " +
                $"Add [{discriminator}] = typeof({expectedType.Name}) to the map.");

            Assert.Equal(expectedType, registeredType);
        }
    }

    // ---------------------------------------------------------------
    // 6. ReportingLine model has expected properties and is sealed
    // ---------------------------------------------------------------
    [Fact]
    public void ReportingLine_Model_IsSealed()
    {
        Assert.True(typeof(ReportingLineModel).IsSealed,
            "ReportingLine must be a sealed class.");
    }

    [Fact]
    public void ReportingLine_Model_HasExpectedProperties()
    {
        var reportingLineId = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2024, 2, 15);
        var effectiveTo = new DateOnly(2024, 12, 31);

        var line = new ReportingLineModel
        {
            ReportingLineId = reportingLineId,
            EmployeeId = "emp010",
            ManagerId = "mgr05",
            TreeRootOrgId = "STY02",
            Relationship = "PRIMARY",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            Source = "MANUAL",
            Version = 1,
            CreatedBy = "admin01",
        };

        Assert.Equal(reportingLineId, line.ReportingLineId);
        Assert.Equal("emp010", line.EmployeeId);
        Assert.Equal("mgr05", line.ManagerId);
        Assert.Equal("STY02", line.TreeRootOrgId);
        Assert.Equal("PRIMARY", line.Relationship);
        Assert.Equal(effectiveFrom, line.EffectiveFrom);
        Assert.Equal(effectiveTo, line.EffectiveTo);
        Assert.Equal("MANUAL", line.Source);
        Assert.Equal(1, line.Version);
        Assert.Equal("admin01", line.CreatedBy);
    }

    [Fact]
    public void ReportingLine_Model_EffectiveTo_IsNullable()
    {
        var line = new ReportingLineModel
        {
            ReportingLineId = Guid.NewGuid(),
            EmployeeId = "emp011",
            ManagerId = "mgr06",
            TreeRootOrgId = "STY02",
            Relationship = "SECONDARY",
            EffectiveFrom = new DateOnly(2024, 1, 1),
            EffectiveTo = null,
            Source = "CSV_IMPORT",
            Version = 1,
            CreatedBy = "system",
        };

        Assert.Null(line.EffectiveTo);
    }

    [Fact]
    public void ReportingLine_Model_AllPropertiesAreInitOnly()
    {
        var properties = typeof(ReportingLineModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        foreach (var prop in properties)
        {
            var setter = prop.SetMethod;
            Assert.NotNull(setter);
            // Init-only setters have the IsExternalInit modreq in their return type custom modifiers
            var modifiers = setter!.ReturnParameter.GetRequiredCustomModifiers();
            Assert.Contains(
                modifiers,
                m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }
    }

    // ---------------------------------------------------------------
    // S49 TASK-4911: FallbackTraversalWarning — round-trip + registration
    // ---------------------------------------------------------------

    [Fact]
    public void EventSerializer_RoundTrips_FallbackTraversalWarning()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 25, 9, 0, 0, DateTimeKind.Utc);

        var original = new FallbackTraversalWarning
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "system",
            ActorRole = "GLOBAL_ADMIN",
            CorrelationId = correlationId,
            EmployeeId = "emp001",
            ResolvedManagerId = "mgr03",
            Depth = 4,
            TreeRootOrgId = "STY02",
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("FallbackTraversalWarning", json);

        var result = Assert.IsType<FallbackTraversalWarning>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("FallbackTraversalWarning", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("system", result.ActorId);
        Assert.Equal("GLOBAL_ADMIN", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("emp001", result.EmployeeId);
        Assert.Equal("mgr03", result.ResolvedManagerId);
        Assert.Equal(4, result.Depth);
        Assert.Equal("STY02", result.TreeRootOrgId);
    }

    [Fact]
    public void EventSerializer_Registers_FallbackTraversalWarning()
    {
        var mapField = typeof(EventSerializer).GetField(
            "EventTypeMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(mapField);
        var map = (IReadOnlyDictionary<string, Type>)mapField!.GetValue(null)!;

        Assert.True(
            map.TryGetValue("FallbackTraversalWarning", out var registeredType),
            "EventSerializer.EventTypeMap is missing discriminator 'FallbackTraversalWarning'. " +
            "Add [FallbackTraversalWarning] = typeof(FallbackTraversalWarning) to the map.");

        Assert.Equal(typeof(FallbackTraversalWarning), registeredType);
    }

    // ---------------------------------------------------------------
    // S50 TASK-5010: Enforcement toggle — round-trip + model tests
    // ---------------------------------------------------------------

    [Fact]
    public void PeriodApproved_RoundTrips_ExplicitFallbackConfirmation()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 25, 14, 0, 0, DateTimeKind.Utc);

        var original = new PeriodApproved
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "mgr01",
            ActorRole = "MANAGER",
            CorrelationId = correlationId,
            PeriodId = periodId,
            EmployeeId = "emp001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            ApprovedBy = "mgr01",
            ExplicitFallbackConfirmation = true,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("PeriodApproved", json);

        var result = Assert.IsType<PeriodApproved>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(periodId, result.PeriodId);
        Assert.Equal("emp001", result.EmployeeId);
        Assert.Equal("AFD01", result.OrgId);
        Assert.Equal("mgr01", result.ApprovedBy);
        Assert.True(result.ExplicitFallbackConfirmation,
            "ExplicitFallbackConfirmation must survive serialization round-trip when set to true.");
    }

    [Fact]
    public void PeriodRejected_RoundTrips_ExplicitFallbackConfirmation()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 25, 14, 30, 0, DateTimeKind.Utc);

        var original = new PeriodRejected
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "mgr02",
            ActorRole = "MANAGER",
            CorrelationId = correlationId,
            PeriodId = periodId,
            EmployeeId = "emp002",
            OrgId = "AFD02",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            RejectedBy = "mgr02",
            RejectionReason = "Incomplete entries",
            ExplicitFallbackConfirmation = true,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("PeriodRejected", json);

        var result = Assert.IsType<PeriodRejected>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(periodId, result.PeriodId);
        Assert.Equal("emp002", result.EmployeeId);
        Assert.Equal("AFD02", result.OrgId);
        Assert.Equal("mgr02", result.RejectedBy);
        Assert.Equal("Incomplete entries", result.RejectionReason);
        Assert.True(result.ExplicitFallbackConfirmation,
            "ExplicitFallbackConfirmation must survive serialization round-trip when set to true.");
    }

    [Fact]
    public void PeriodApproved_DefaultFalse_ExplicitFallbackConfirmation()
    {
        var original = new PeriodApproved
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "emp003",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 6, 1),
            PeriodEnd = new DateOnly(2026, 6, 30),
            ApprovedBy = "mgr01",
            // ExplicitFallbackConfirmation NOT set — should default to false
        };

        Assert.False(original.ExplicitFallbackConfirmation,
            "ExplicitFallbackConfirmation must default to false when not explicitly set.");

        // Also verify round-trip preserves the false default
        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("PeriodApproved", json);
        var result = Assert.IsType<PeriodApproved>(deserialized);
        Assert.False(result.ExplicitFallbackConfirmation,
            "ExplicitFallbackConfirmation=false must survive serialization round-trip.");
    }

    [Fact]
    public void TreeSettings_Model_HasExpectedProperties()
    {
        var now = DateTime.UtcNow;
        var settings = new TreeSettings
        {
            TreeRootOrgId = "STY02",
            EnforcementMode = "REQUIRED",
            Version = 3,
            UpdatedBy = "admin01",
            UpdatedAt = now,
        };

        Assert.Equal("STY02", settings.TreeRootOrgId);
        Assert.Equal("REQUIRED", settings.EnforcementMode);
        Assert.Equal(3, settings.Version);
        Assert.Equal("admin01", settings.UpdatedBy);
        Assert.Equal(now, settings.UpdatedAt);

        // Verify the model is sealed
        Assert.True(typeof(TreeSettings).IsSealed,
            "TreeSettings must be a sealed class.");

        // Verify all expected property names exist
        var propertyNames = typeof(TreeSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Contains("TreeRootOrgId", propertyNames);
        Assert.Contains("EnforcementMode", propertyNames);
        Assert.Contains("Version", propertyNames);
        Assert.Contains("UpdatedBy", propertyNames);
        Assert.Contains("UpdatedAt", propertyNames);
        Assert.Equal(5, propertyNames.Count);
    }

    // ---------------------------------------------------------------
    // S51 TASK-5109: Self-service delegation — ScheduledExpiry + ReportingLineSelfDelegated
    // ---------------------------------------------------------------

    [Fact]
    public void ReportingLine_ScheduledExpiry_RoundTrips()
    {
        var expiry = new DateOnly(2026, 6, 15);
        var line = new ReportingLineModel
        {
            ReportingLineId = Guid.NewGuid(),
            EmployeeId = "emp001",
            ManagerId = "mgr01",
            TreeRootOrgId = "STY02",
            Relationship = "ACTING",
            EffectiveFrom = new DateOnly(2026, 5, 1),
            Source = "SELF_DELEGATION",
            Version = 1,
            ScheduledExpiry = expiry,
            CreatedBy = "mgr01",
        };

        Assert.Equal(expiry, line.ScheduledExpiry);
    }

    [Fact]
    public void ReportingLine_ScheduledExpiry_IsNullable()
    {
        var line = new ReportingLineModel
        {
            ReportingLineId = Guid.NewGuid(),
            EmployeeId = "emp002",
            ManagerId = "mgr02",
            TreeRootOrgId = "STY02",
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 5, 1),
            Source = "MANUAL",
            Version = 1,
            CreatedBy = "admin01",
        };

        Assert.Null(line.ScheduledExpiry);
    }

    [Fact]
    public void ReportingLineSelfDelegated_RoundTrips()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 5, 25, 15, 0, 0, DateTimeKind.Utc);

        var original = new ReportingLineSelfDelegated
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "mgr01",
            ActorRole = "MANAGER",
            CorrelationId = correlationId,
            BatchId = batchId,
            DelegatingManagerId = "mgr01",
            ActingManagerId = "mgr02",
            DelegatedCount = 5,
            SkippedCount = 1,
            EffectiveFrom = new DateOnly(2026, 5, 25),
            EffectiveTo = new DateOnly(2026, 6, 15),
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("ReportingLineSelfDelegated", json);

        var result = Assert.IsType<ReportingLineSelfDelegated>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("ReportingLineSelfDelegated", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("mgr01", result.ActorId);
        Assert.Equal("MANAGER", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(batchId, result.BatchId);
        Assert.Equal("mgr01", result.DelegatingManagerId);
        Assert.Equal("mgr02", result.ActingManagerId);
        Assert.Equal(5, result.DelegatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(new DateOnly(2026, 5, 25), result.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 6, 15), result.EffectiveTo);
    }

    [Fact]
    public void EventSerializer_Registers_ReportingLineSelfDelegated()
    {
        var mapField = typeof(EventSerializer).GetField(
            "EventTypeMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(mapField);
        var map = (IReadOnlyDictionary<string, Type>)mapField!.GetValue(null)!;

        Assert.True(
            map.TryGetValue("ReportingLineSelfDelegated", out var registeredType),
            "EventSerializer.EventTypeMap is missing discriminator 'ReportingLineSelfDelegated'. " +
            "Add [ReportingLineSelfDelegated] = typeof(ReportingLineSelfDelegated) to the map.");

        Assert.Equal(typeof(ReportingLineSelfDelegated), registeredType);
    }
}
