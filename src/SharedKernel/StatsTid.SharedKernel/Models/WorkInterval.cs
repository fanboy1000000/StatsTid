namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Immutable value object representing a single self-recorded work interval for a day.
/// Start/End are wall-clock strings in "HH:mm" or "HH:mm:ss" form (no date component).
/// Used as the interval shape inside the <c>WorkTimeRegistered</c> domain event.
/// </summary>
public sealed class WorkInterval
{
    public required string Start { get; init; }
    public required string End { get; init; }
}
