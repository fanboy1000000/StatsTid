using System.Reflection;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
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
}
