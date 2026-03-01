using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Events;

public class DomainEventBaseActorTests
{
    [Fact]
    public void ActorFields_AreNullByDefault()
    {
        var evt = new TimeEntryRegistered
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2024, 6, 1),
            Hours = 7.4m,
            AgreementCode = "AC",
            OkVersion = "OK24"
        };

        Assert.Null(evt.ActorId);
        Assert.Null(evt.ActorRole);
        Assert.Null(evt.CorrelationId);
    }

    [Fact]
    public void ActorFields_CanBeSet()
    {
        var correlationId = Guid.NewGuid();
        var evt = new TimeEntryRegistered
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2024, 6, 1),
            Hours = 7.4m,
            AgreementCode = "AC",
            OkVersion = "OK24",
            ActorId = "EMP042",
            ActorRole = "Manager",
            CorrelationId = correlationId
        };

        Assert.Equal("EMP042", evt.ActorId);
        Assert.Equal("Manager", evt.ActorRole);
        Assert.Equal(correlationId, evt.CorrelationId);
    }

    [Fact]
    public void EventSerializer_RoundTrip_PreservesActorFields()
    {
        var correlationId = Guid.NewGuid();
        var original = new TimeEntryRegistered
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2024, 6, 1),
            Hours = 7.4m,
            AgreementCode = "HK",
            OkVersion = "OK24",
            ActorId = "EMP099",
            ActorRole = "Admin",
            CorrelationId = correlationId
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("TimeEntryRegistered", json);

        Assert.IsType<TimeEntryRegistered>(deserialized);
        var result = (TimeEntryRegistered)deserialized;
        Assert.Equal("EMP099", result.ActorId);
        Assert.Equal("Admin", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
    }
}
