namespace StatsTid.SharedKernel.Models;

public sealed class TimerSession
{
    public required Guid SessionId { get; init; }
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required DateTime CheckInAt { get; init; }
    public DateTime? CheckOutAt { get; init; }
    public bool IsActive { get; init; } = true;
}
