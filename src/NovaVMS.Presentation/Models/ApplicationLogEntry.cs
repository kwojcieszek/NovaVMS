namespace NovaVMS.Presentation.Models;

public sealed record ApplicationLogEntry(string Timestamp, string ProcessId, string ThreadId, string Source, string Details)
{
    public string Summary => Details.Split('\n', 2)[0].TrimEnd();
    public string FullText => Timestamp.Length == 0 ? Details
        : $"[{Timestamp}] [PID {ProcessId}, thread {ThreadId}] {Source}{Environment.NewLine}{Details}";
}
