namespace NovaVMS.Presentation.Models;

public sealed record ApplicationLogEntry
{
    public string Timestamp { get; init; }

    public string ProcessId { get; init; }

    public string ThreadId { get; init; }

    public string Source { get; init; }

    public string Details { get; init; }

    public string Summary => Details.Split('\n', 2)[0].TrimEnd();

    public string FullText => Timestamp.Length == 0 ? Details
        : $"[{Timestamp}] [PID {ProcessId}, thread {ThreadId}] {Source}{Environment.NewLine}{Details}";

    public ApplicationLogEntry(string timestamp, string processId, string threadId, string source, string details)
    {
        Timestamp = timestamp;
        ProcessId = processId;
        ThreadId = threadId;
        Source = source;
        Details = details;
    }
}
