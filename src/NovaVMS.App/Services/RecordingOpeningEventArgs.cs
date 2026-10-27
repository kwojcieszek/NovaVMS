namespace NovaVMS.App.Services;

/// <summary>Allows live streams to finish closing before a recording session starts.</summary>
public sealed class RecordingOpeningEventArgs : EventArgs
{
    private readonly List<Task> _preparations = new();
    public void WaitFor(Task preparation) => _preparations.Add(preparation);
    internal Task WhenReady => Task.WhenAll(_preparations);
}
