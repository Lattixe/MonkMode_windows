namespace MonkMode.Models;

/// <summary>
/// Request to start a focus session (from launcher).
/// </summary>
public class FocusSessionRequest : EventArgs
{
    public required string TaskName { get; init; }
    public required int DurationMinutes { get; init; }
}

/// <summary>
/// Result of a completed focus session.
/// </summary>
public class FocusSessionResult : EventArgs
{
    public required string TaskName { get; init; }
    public required TimeSpan PlannedDuration { get; init; }
    public required TimeSpan ActualDuration { get; init; }
    public required bool Completed { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
}
