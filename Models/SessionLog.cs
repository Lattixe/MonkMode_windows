namespace MonkMode.Models;

/// <summary>
/// Represents a completed focus session for persistence and analysis.
/// </summary>
public class SessionLog
{
    public int Id { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public int IntensityLevel { get; set; }
    public int InterventionCount { get; set; }
    public int FlowRating { get; set; } // 1-5 user rating
    public string? UserNotes { get; set; }
    public string? AiCoachResponse { get; set; }
    public List<string> BlockedProcesses { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
}

/// <summary>
/// Represents a blocked process intervention event.
/// </summary>
public class InterventionLog
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public DateTime Timestamp { get; set; }
    public string InterventionType { get; set; } = string.Empty; // "ProcessKill", "DNSBlock"
    public string TargetName { get; set; } = string.Empty; // Process name or domain
}

/// <summary>
/// User-defined blocklist configuration.
/// </summary>
public class BlocklistConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "Default";
    public List<string> Processes { get; set; } = new();
    public List<string> Domains { get; set; } = new();
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
